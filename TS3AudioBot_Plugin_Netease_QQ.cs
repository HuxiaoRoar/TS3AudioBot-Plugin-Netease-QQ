using IniParser;
using IniParser.Model;
using MusicAPI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using TS3AudioBot;
using TS3AudioBot.Audio;
using TS3AudioBot.CommandSystem;
using TS3AudioBot.Config;
using TS3AudioBot.Plugins;
using TS3AudioBot.ResourceFactories;
using TSLib.Full;
using TSLib.Full.Book;
using TSLib.Helper;
using TSLib.Scheduler;
using static System.Net.Mime.MediaTypeNames;


namespace TS3AudioBot_Plugin_Netease_QQ
{
    public class Netease_QQ_plugin : IBotPlugin
    {
        private PlayManager playManager;
        private Ts3Client ts3Client;
        private InvokerData invokerData;
        private Player player;
        private Connection connection;

        private bool isPlayingNeteaseOrQQ = false;

        // 机器人在连接中的名字
        private string botname_connect;
        private string botname_connect_before;
        // config
        readonly private static string config_file_name = "netease_qq_config.ini";
        private static string iniPath;
        FileIniDataParser plugin_config_parser;
        IniData plugin_config;

        // 网易云api地址
        private static string netease_api_address;
        private static string netease_cookies;
        // QQ音乐api地址
        private static string qqmsuic_api_address;
        private static string qqmusic_cookies;
        private static string qqmusic_fm_id; 

        // API
        private static MusicAPI.MusicAPI musicapi;
        // 播放
        // 播放列表, 结构List<"id": <id>,"music_type" :<音乐API选择(0为网易云, 1为QQ音乐)>>
        private List<Dictionary<string, string>> PlayList = new List<Dictionary<string, string>>();
        // 播放类型, 0:常规PlayList播放, 1:私人FM(仅限于网易云音乐)
        private int play_type = 0;
        // PlayList播放的播放模式, 0:顺序播放(到末尾自动暂停), 1:单曲循环, 2:顺序循环, 3:随机
        private int play_mode = 1;
        // 当前播放的FM平台, 0:网易云, 1:QQ音乐
        private int fm_platform = 0;
        // PlayList的播放index
        private int play_index = 0;
        // 是否阻塞
        private bool isObstruct = false;
        // 当前播放的歌词
        private List<Dictionary<TimeSpan, string>> Lyric = new List<Dictionary<TimeSpan, string>>();
        // 上一个歌词
        private string lyric_before;
        // 歌词线程
        private static readonly int lyric_refresh_time = 400;
        private System.Timers.Timer Lyric_thread;
        private readonly DedicatedTaskScheduler scheduler;  // 用于在主线程调用ts3函数
        private bool isLyric = false;
        private string lyric_id_now;
        // 等待频道无人时间
        private readonly static int max_wait_alone = 30;
        private int waiting_time = 0;
        private void ForceLoadImageSharpDependency()
        {
            try
            {
                // 这是一个轻量级的操作，但足以触发静态构造函数。
                var config = SixLabors.ImageSharp.Configuration.Default;
                //Console.WriteLine($"[Netease QQ Plugin] Successfully pre-loaded dependency: SixLabors.ImageSharp, Version={typeof(SixLabors.ImageSharp.Image).Assembly.GetName().Version}");
            }
            catch (TypeInitializationException tie)
            {
                // TypeInitializationException 是关键，我们需要看它的内部异常。
                Console.WriteLine($"[Netease QQ Plugin Critical Error] ImageSharp failed to initialize. This is often due to a missing sub-dependency.");
                Console.WriteLine($"[Netease QQ Plugin Critical Error] TypeInitializerException: {tie.Message}");
                // 递归打印所有内部异常，找到根本原因。
                Exception inner = tie.InnerException;
                int count = 1;
                while (inner != null)
                {
                    Console.WriteLine($"[Netease QQ Plugin Critical Error]   Inner Exception ({count++}): {inner.GetType().Name} - {inner.Message}");
                    Console.WriteLine($"[Netease QQ Plugin Critical Error]   Stack Trace: {inner.StackTrace}");
                    inner = inner.InnerException;
                }
            }
            catch (Exception ex)
            {
                // 捕获其他可能的异常。
                Console.WriteLine($"[Netease QQ Plugin Critical Error] An unexpected error occurred while force-loading ImageSharp: {ex.ToString()}");
            }
        }
        //--------------------------获取audio bot数据--------------------------
        public Netease_QQ_plugin(PlayManager playManager, Ts3Client ts3Client, Player player, Connection connection, ConfBot confBot, DedicatedTaskScheduler scheduler)
        {
            this.playManager = playManager;
            this.ts3Client = ts3Client;
            this.player = player;
            this.connection = connection;
            this.botname_connect = confBot.Connect.Name;
            this.scheduler = scheduler;
        }
        //--------------------------初始化--------------------------
        public async void Initialize()
        {
            // audiobot

            // 读取配置文件
            // 判断操作系统            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows环境
                iniPath = "plugins/" + config_file_name;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string location = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                if (System.IO.File.Exists("/.dockerenv"))
                {
                    // Docker环境
                    iniPath = location + "/data/plugins/" + config_file_name;
                }
                else
                {
                    // Linux环境
                    iniPath = location + "/plugins/" + config_file_name;
                }
            }
            else
            {
                throw new NotSupportedException("不支持的操作系统");
            }
            // 检查配置文件是否存在
            if (!System.IO.File.Exists(iniPath))
            {
                await ts3Client.SendChannelMessage($"配置文件 {iniPath} 未找到");
                throw new FileNotFoundException($"配置文件 {iniPath} 未找到");
            }
            try
            {
                // 读取配置
                plugin_config_parser = new FileIniDataParser();
                plugin_config = new IniData();
                plugin_config = plugin_config_parser.ReadFile(iniPath);
            }
            catch (Exception ex)
            {
                await ts3Client.SendChannelMessage($"读取配置文件失败: {ex.Message}");
                throw new Exception($"读取配置文件失败: {ex.Message}");
            }
            ForceLoadImageSharpDependency();
            //    Console.WriteLine($"NeteaseQQPlugin: Forcing load of ImageSharp, Version={typeof(SixLabors.ImageSharp.Image).Assembly.GetName().Version}");

            // 设置配置
            netease_api_address = plugin_config["netease"]["neteaseAPI"];
            netease_api_address = string.IsNullOrEmpty(netease_api_address) ? "http://127.0.0.1:3000" : netease_api_address;
            netease_cookies = plugin_config["netease"]["cookies"];
            netease_cookies = netease_cookies.Trim(new char[] { '"' });
            netease_cookies = string.IsNullOrEmpty(netease_cookies) ? "" : netease_cookies;

            qqmsuic_api_address = plugin_config["qq"]["qqAPI"];
            qqmsuic_api_address = string.IsNullOrEmpty(qqmsuic_api_address) ? "http://127.0.0.1:3300" : qqmsuic_api_address;
            qqmusic_cookies = plugin_config["qq"]["cookies"];
            qqmusic_cookies = qqmusic_cookies.Trim(new char[] { '"' });
            qqmusic_cookies = string.IsNullOrEmpty(qqmusic_cookies) ? "" : qqmusic_cookies;

            qqmsuic_api_address = plugin_config["qq"]["qqAPI"];
            qqmsuic_api_address = string.IsNullOrEmpty(qqmsuic_api_address) ? "http://127.0.0.1:3300" : qqmsuic_api_address;
            qqmusic_cookies = plugin_config["qq"]["cookies"];
            qqmusic_cookies = qqmusic_cookies.Trim(new char[] { '"' });
            qqmusic_cookies = string.IsNullOrEmpty(qqmusic_cookies) ? "" : qqmusic_cookies;
            qqmusic_fm_id = plugin_config["qq"]["qqfm"]; 
            qqmusic_fm_id = qqmusic_fm_id.Trim(new char[] { '"' });
            qqmusic_fm_id = string.IsNullOrEmpty(qqmusic_fm_id) ? "99" : qqmusic_fm_id;

            // 保存参数
            musicapi = new MusicAPI.MusicAPI();
            musicapi.SetAddress(netease_api_address, 0);
            musicapi.SetCookies(netease_cookies, 0);
            musicapi.SetAddress(qqmsuic_api_address, 1);
            musicapi.SetCookies(qqmusic_cookies, 1);

            // 添加audio bot 事件
            playManager.PlaybackStopped += OnSongStop;
            ts3Client.OnAloneChanged += OnAlone;
            playManager.AfterResourceStarted += AfterSongStart;




            // 启动歌词线程
            StartLyric(true);

            // 获取当前正在运行的程序集
            var version = Assembly.GetExecutingAssembly().GetName().Version;

            // 将版本号格式化成 "主版本.次版本.生成号" 的形式，忽略最后的修订号
            string displayVersion = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

            // 欢迎词
            await ts3Client.SendChannelMessage($"网易、QQ插件加载完毕！当前版本：v{displayVersion}");
        }
        //--------------------------歌词线程--------------------------
        private void StartLyric(bool enable)
        {// 启动或者关闭歌词线程
            if (enable)
            {
                if (Lyric_thread == null)
                {
                    Lyric_thread = new System.Timers.Timer(lyric_refresh_time);
                    Lyric_thread.Elapsed += (s, args) => LyricWork(ts3Client);
                    Lyric_thread.AutoReset = true;
                    Lyric_thread.Enabled = true; // 显式启用
                }
                Lyric_thread.Start();
            }
            else
            {
                Lyric_thread.Stop();
            }
        }
        private async void LyricWork(Ts3Client ts3c)
        {
            try
            {
                if (isLyric && this.PlayList.Count != 0 && !player.Paused && playManager.IsPlaying)
                {
                    await scheduler.InvokeAsync(async () =>
                    {
                        string string_id;
                        PlayList[play_index].TryGetValue("id", out string_id);
                        //Console.WriteLine("1 id:" + string_id); 频繁打印歌词 感觉是调试 先注释掉
                        if (Lyric.Count == 0 || lyric_id_now != string_id)
                        {// 获取歌词
                            Lyric.Clear();
                            lyric_before = "";
                            lyric_id_now = string_id;
                            await GetLyricNow();
                        }
                        // 开始刷新歌词
                        string lyric_now = "";
                        TimeSpan now = (TimeSpan)player.Position;
                        TimeSpan length = (TimeSpan)player.Length;
                        for (int i = 0; i < Lyric.Count; i++)
                        {
                            if (now > Lyric[i].First().Key)
                            {
                                lyric_now = Lyric[i][Lyric[i].First().Key];
                                long p_now, p_length;
                                p_now = (long)((TimeSpan)player.Position).TotalSeconds;
                                p_length = (long)((TimeSpan)player.Length).TotalSeconds;
                                lyric_now += $" [{p_now}/{p_length}s]";
                            }
                            else
                            {
                                break;
                            }
                        }
                        if (lyric_now != lyric_before)
                        {
                            await ts3c.ChangeDescription(lyric_now);
                        }
                        lyric_before = lyric_now;
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"歌词线程中出现错误: {ex.Message}");
            }
        }
        //--------------------------歌单操作--------------------------
        public async Task PlayListPlayNow()
        {// 播放歌单中的歌曲，带歌词线程，请在指令的最后调用
            Lyric.Clear();
            // 歌单 播放当前index 的歌
            if (PlayList.Count != 0 && play_index < PlayList.Count)
            {
                string string_id = "";
                string string_music_type = "";
                int music_type = 0;
                PlayList[play_index].TryGetValue("id", out string_id);
                PlayList[play_index].TryGetValue("music_type", out string_music_type);
                int.TryParse(string_music_type, out music_type);
                try
                {
                    await PlayMusic(string_id, (int)music_type);
                }
                catch (Exception e)
                {
                    throw new Exception("(PlayMusic)中: " + e.Message);
                }
            }
        }
        public async Task PlayFMNow()
        {

            try
            {
                isObstruct = true;
                await ts3Client.SendChannelMessage("正在获取新的FM推荐歌曲...");

                if (fm_platform == 0) // 网易云FM
                {
                    PlayList.Clear();
                    string fm_id = await musicapi.GetFMSongId();
                    if (string.IsNullOrEmpty(fm_id))
                    {
                        await ts3Client.SendChannelMessage("无法获取网易云FM歌曲ID。");
                        return;
                    }
                    // 使用现有的 PlayListAdd 方法添加单曲，它会自动处理播放
                    await PlayListAdd(fm_id, 0);
                    isObstruct = false;
                }
                else if (fm_platform == 1) // QQ音乐FM
                {
                    var songs = await musicapi.GetQQFMSongs(qqmusic_fm_id);
                    if (songs == null || songs.Count == 0)
                    {
                        await ts3Client.SendChannelMessage("无法获取QQ电台歌曲，或电台为空。");
                        return;
                    }

                    PlayList.Clear();
                    foreach (var song in songs)
                    {
                        PlayList.Add(new Dictionary<string, string> {
                    { "id", song["id"] },
                    { "music_type", "1" },
                    { "name", song["name"] },
                    { "author", song["author"] }
                });
                    }

                    QqFmStations.TryGetValue(qqmusic_fm_id, out string stationName);
                    string stationNameDisplay = string.IsNullOrEmpty(stationName) ? $"ID {qqmusic_fm_id}" : stationName;
                    await ts3Client.SendChannelMessage($"QQ电台加载完成，共 {songs.Count} 首歌曲。当前是：{stationNameDisplay}");

                    play_index = 0;
                    await PlayListPlayNow();

                    isObstruct = false;
                }
            }
            catch (InvalidOperationException e)
            {
                await ts3Client.SendChannelMessage($"在获取FM歌曲ID出现错误: {e.Message}");
            }
            catch (ArgumentException e)
            {
                await ts3Client.SendChannelMessage($"在获取FM歌曲ID出现错误: {e.Message}");
            }
        }
        public async Task PlayListAdd(string song_id, int music_type, int idx = -1)
        {
            // 适用于用户的添加单独歌曲，会获取歌曲detail，gd指令是适用于添加批量歌曲
            // 歌单添加歌 同时返回歌名
            string song_name = "", song_author = "";
            Dictionary<string, string> detail;
            try
            {
                detail = await musicapi.GetSongDetail(song_id, music_type);
                detail.TryGetValue("name", out song_name);
                detail.TryGetValue("author", out song_author);
                // 添加歌单
                if (idx < 0) { idx = PlayList.Count; }
                PlayList.Insert(idx, new Dictionary<string, string> {
                        {"id", song_id },
                        { "music_type", music_type.ToString() },
                        { "name", song_name },
                        {"author", song_author }
                 });
                string string_type = music_type == 0 ? "网易云音乐" : music_type == 1 ? "QQ音乐" : "";
                string url = music_type == 0 ? $"https://music.163.com/#/song?id={song_id}" : music_type == 1 ? "https://y.qq.com/n/ryqq/songDetail/{song_id}" : "";
                await ts3Client.SendChannelMessage($"{string_type}:{song_name}-{song_author}添加成功。索引: {idx + 1}。"); 
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"在获取歌曲详细信息出现错误: {e.Message}");
            }
            try
            {
                if (PlayList.Count == 1)
                {
                    // 直接播放
                    play_index = 0;
                    await PlayListPlayNow();
                }
                else if (!playManager.IsPlaying)
                {// 自动播放
                    play_index = PlayList.Count - 1;
                    await PlayListPlayNow();
                }
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"在播放歌曲(PlayListPlayNow)时候出现了错误: {e.Message}");
            }
        }
        public async Task GetLyricNow()
        {
            if (PlayList.Count != 0 && play_index < PlayList.Count)
            {
                string string_id = "";
                string string_music_type = "";
                int music_type = 0;
                PlayList[play_index].TryGetValue("id", out string_id);
                PlayList[play_index].TryGetValue("music_type", out string_music_type);
                int.TryParse(string_music_type, out music_type);
                try
                {
                    List<Dictionary<TimeSpan, string>> res = await musicapi.GetSongLyric(string_id, music_type);
                    Lyric = res.ToList();
                }
                catch (Exception e)
                {
                    await ts3Client.SendChannelMessage($"在获取歌词过程中出现错误: {e.Message}");
                }
            }
        }
        public async Task PlayListNext()
        {
            // 歌单下一首
            if (PlayList.Count == 0)
            {
                await ts3Client.SendChannelMessage("歌单无歌");
                return;
            }
            
            if (play_type == 0)
                {
                    // 常规歌单
                    if (play_mode == 1)// ai建议删除 || play_mode == 2 // 顺序播放
                {
                        // 列表顺序播放, 或者单曲循环
                        if (play_index + 1 >= PlayList.Count)
                        {
                            // 列表末尾
                            _ = MainCommands.CommandBotName(ts3Client, botname_connect);
                            await ts3Client.ChangeDescription("");
                            isPlayingNeteaseOrQQ = false;
                            await ts3Client.SendChannelMessage("歌单已经到底了");
                        }
                        else
                        {
                            // 非末尾
                            play_index += 1;
                            await PlayListPlayNow();
                        }
                    }
                    else if (play_mode == 3)
                    {
                        // 循环播放
                        if (play_index + 1 >= PlayList.Count)
                        {
                            // 列表末尾
                            play_index = 0;
                            await PlayListPlayNow();
                        }
                        else
                        {
                            // 非末尾
                            play_index += 1;
                            await PlayListPlayNow();
                        }
                    }
                    else if (play_mode == 4)
                    {
                        // 列表随机循环播放
                        Random random = new Random();
                        play_index = random.Next(0, PlayList.Count);
                        await PlayListPlayNow();
                    }
                    else if (play_mode == 5)
                    {
                        // 顺序销毁模式
                        PlayList.Remove(PlayList[play_index]);
                        if (PlayList.Count == 0)
                        {
                            _ = MainCommands.CommandBotName(ts3Client, botname_connect);
                            await ts3Client.ChangeDescription("");
                            isPlayingNeteaseOrQQ = false;
                            await ts3Client.SendChannelMessage("歌单已经到底了");
                        }
                        else
                        {
                            await PlayListPlayNow();
                        }
                    }
                    else if (play_mode == 6)
                    {
                        // 随机销毁模式
                        PlayList.Remove(PlayList[play_index]);
                        if (PlayList.Count == 0)
                        {
                            _ = MainCommands.CommandBotName(ts3Client, botname_connect);
                            await ts3Client.ChangeDescription("");
                            isPlayingNeteaseOrQQ = false;
                            await ts3Client.SendChannelMessage("歌单已经到底了");
                        }
                        else
                        {
                            Random random = new Random();
                            play_index = random.Next(0, PlayList.Count);
                            await PlayListPlayNow();
                        }
                    }
                }
                else if (play_type == 1)
                if (play_index + 1 < PlayList.Count)
                {
                    // 1. 如果当前FM列表还没播完 (适用于QQ FM)
                    play_index++;
                    await ts3Client.SendChannelMessage($"FM下一首({play_index + 1}/{PlayList.Count})");
                    await PlayListPlayNow();
                }
                else
                {
                    // 2. 如果当前FM列表已经播完 (适用于网易云FM 和 QQ FM的最后一首)
                    // 就调用统一的FM播放中心来获取新歌
                    await PlayFMNow();
                }
        }       
        public async Task PlayListPre()
        {
            // 播放上一首
            if (play_type == 0)
            {
                if (PlayList.Count <= 1)
                {
                    await ts3Client.SendChannelMessage("无法上一首播放");
                }
                else
                {
                    if (play_index == 0)
                    {
                        await ts3Client.SendChannelMessage("已经到歌单最顶部了");
                    }
                    else
                    {
                        play_index--;
                        await PlayListPlayNow();
                    }
                }
            }
            else if (play_type == 1)
            {
                await ts3Client.SendChannelMessage("FM模式不支持上一首播放");
            }
        }
        public async Task PlayListShow(int page = 1)
        {
            // 展示歌单
            StringBuilder playlist_string_builder = new StringBuilder();
            playlist_string_builder.AppendLine("歌单如下:");
            // 纯文本表头
            playlist_string_builder.AppendLine(FormatLine("索引", "歌曲名", "歌手", "来源", 10, 61, 35, 15));
            // 用一行分隔符来增强可读性s
            playlist_string_builder.AppendLine("---------------------------------------------------------------------------------------------");

            for (int i = (page - 1) * 10; i < PlayList.Count && i < page * 10; i++)
            {
                // 遍历列表
                string index_string, name_string, author_string, music_type_string;
                index_string = i == play_index ? (i + 1).ToString() + "*" : (i + 1).ToString();
                PlayList[i].TryGetValue("name", out name_string);
                PlayList[i].TryGetValue("author", out author_string);
                PlayList[i].TryGetValue("music_type", out music_type_string);
                music_type_string = music_type_string == "0" ? "网易云" : music_type_string == "1" ? "QQ音乐" : "未知";
                playlist_string_builder.AppendLine(FormatLine(index_string, name_string, author_string, music_type_string, 10, 60, 35, 15));
            }
            playlist_string_builder.AppendLine($"第{page}页,共{(PlayList.Count + 9) / 10}页 | 正在播放第{play_index + 1}首,共{PlayList.Count}首歌");
            string playlist_string = playlist_string_builder.ToString();
            await ts3Client.SendChannelMessage(playlist_string);
        }
        static string FormatLine(string index, string name, string author, string musictype, int indexWidth, int nameWidth, int authorWidth, int musictypeWidth)
        {
            // 根据中英文字符宽度调整字符串，并用空格填充
            return $"{PadWithSpaces(index, indexWidth)} " + // 增加一个空格作为分隔
                   $"{PadWithSpaces(name, nameWidth)} " +
                   $"{PadWithSpaces(author, authorWidth)} " +
                   $"{PadWithSpaces(musictype, musictypeWidth)}";
        }
        static string PadWithSpaces(string input, int maxWidth)
        {
            if (string.IsNullOrEmpty(input))
                return new string(' ', maxWidth);

            double currentWidth = 0; // 使用 double 来累加小数宽度
            StringBuilder result = new StringBuilder();

            // 遍历输入字符串，处理字符宽度
            foreach (char c in input)
            {
                double charWidth = GetCharWidth(c);

                // 检查是否会超出最大宽度，如果会，则截断并添加省略号
                if (currentWidth + charWidth > maxWidth)
                {
                    // 为了防止省略号本身超出，我们先移除最后一个字符（如果存在）
                    if (result.Length > 0)
                    {
                        // 计算被移除字符的宽度，以便正确地重新计算当前宽度
                        char lastChar = result[result.Length - 1];
                        currentWidth -= GetCharWidth(lastChar);
                        result.Remove(result.Length - 1, 1);
                    }
                    // 添加省略号并更新宽度
                    result.Append('…');
                    currentWidth += GetCharWidth('…');
                    break;
                }

                result.Append(c);
                currentWidth += charWidth;
            }
            int roundedWidth = (int)Math.Floor(currentWidth);
            // 用空格（宽度为1）填充剩余部分
            int remainingWidth = maxWidth - roundedWidth;
            if (remainingWidth > 0)
            {
                result.Append(new string(' ', remainingWidth));
            }

            return result.ToString();
        }
        // 新增：根据字符类型获取其显示宽度
        static double GetCharWidth(char c)
        {
            if (IsChinese(c))
            {
                return 4; // 中文等价4个空格
            }
            // 处理西文字符和数字
            switch (c)
            {
                // 5档 (最宽)
                case 'M':
                case 'W':
                case'm':
                case '《':
                case '》':
                case '【':
                case '】':
                    return 4;

                // 4档 (较宽)
                case '@':
                case 'Q':
                case 'O':
                case 'G':
                case 'D':                
                case 'H':
                case 'V':
                case 'U':                
                case 'N':               
                    return 3;

                // 3档 (中等)
                case 'b':
                case 'd':
                case 'g':
                case 'h':
                case 'n':
                case 'o':
                case 'p':
                case 'q':                
                case 'u':
                case 'A':
                case 'B':
                case 'C':
                case 'K':
                case 'P':
                case 'R':
                case 'X':
                case 'Y':
                    return 2.5;

                // 1档 (最窄)
                case 'i':
                case 'j':
                case 'l':
                case 't':
                case 'f':
                case '.':
                case ',':
                case ';':
                case ':':
                case '!':               
                    return 1;
            }


            // 2档 (较窄)
            if (char.IsUpper(c) || char.IsLower(c) || char.IsDigit(c)|| c == '*' || c == '：')
            {
                return 2; // 其余大小写 字母 数字 等价2个
            }
            
            // 默认其他所有字符（如符号、空格等）为1个
            return 1;
        }
        // 判断字符是否为中文
        static bool IsChinese(char c)
        {
            // 这个范围涵盖了中日韩统一表意文字等主要东亚字符
            if (                 
                 (c >= 0x3040 && c <= 0x309F) || // 日文平假名
                 (c >= 0x30A0 && c <= 0x30FF) || // 日文片假名，韩文字母
                 (c >= 0x4E00 && c <= 0x9FFF) || // CJK统一表意文字
                 (c >= 0xAC00 && c <= 0xD7AF))   // 韩文音节
            {
                return true;
            }
            return false;
        }
        //--------------------------播放歌--------------------------
        public async Task PlayMusic(string Songid, int music_type)
        {
            // 播放歌曲id为Songid的音乐
            Dictionary<string, string> detail = null;
            try
            {// 获取detail
                detail = await musicapi.GetSongDetail(Songid, music_type);
            }
            catch (InvalidOperationException e)
            {
                await ts3Client.SendChannelMessage($"在获取歌曲详细信息出现错误: {e.Message}");
                return;
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"在获取歌曲详细信息出现错误: {e.Message}");
            }

            string songurl = "";
            if (music_type == 0)
            {
                try
                {// 获取url
                    songurl = await musicapi.GetSongUrl(Songid, music_type);
                }
                catch (Exception e)
                {
                    await ts3Client.SendChannelMessage($"在获取歌曲URL出现错误: {e.Message}");
                }
            }
            else if (music_type == 1)
            {
                try
                {// 获取url
                    string mediamid = detail["mediamid"];
                    songurl = await musicapi.GetSongUrl(Songid, music_type, mediamid);
                }
                catch (Exception e)
                {
                    await ts3Client.SendChannelMessage($"在获取歌曲URL出现错误: {e.Message}");
                }
            }
            if (songurl == null || songurl == "")
            {
                await ts3Client.SendChannelMessage("url获取为空");
            }
            else
            {
                // 修改机器人描述
                string songname = "名称获取失败", authorname = "", picurl = "";
                string modename = "";
                string native_url;
                string newname;
                if (detail != null)
                {
                    detail.TryGetValue("name", out songname);
                    detail.TryGetValue("author", out authorname);
                    detail.TryGetValue("picurl", out picurl);
                    // 加入音乐url
                    try
                    {
                        // 使用ts3bot的play播放音乐
                        native_url = "";
                        if (music_type == 0)
                        {
                            native_url = $"https://music.163.com/#/song?id={Songid}";
                        }
                        else if (music_type == 1)
                        {
                            native_url = $"https://y.qq.com/n/ryqq/songDetail/{Songid}";
                        }
                        string fullName = songname + (string.IsNullOrEmpty(authorname) ? "" : $" - {authorname}");
                        var ar = new AudioResource(native_url, fullName, "media")
                          .Add("PlayUri", picurl)
                          .Add("source", "NeteaseQQPlugin");
                        // Console.WriteLine($"cover:{picurl}");
                        await playManager.Play(invokerData, new MediaPlayResource(songurl, ar, await musicapi.HttpGetImage(picurl), false));
                        isPlayingNeteaseOrQQ = true;    
                        // await MainCommands.CommandPlay(playManager, invokerData, songurl);
                    }
                    catch (Exception)
                    {
                        await ts3Client.SendChannelMessage($"歌曲URL无法播放-若为QQ音乐建议重启API: {songurl}");
                        return;
                    }
                    // 修改机器人描述和头像
                    if (play_type == 0)
                    {
                        if (play_mode == 1)
                        {
                            modename = "[顺序]";
                        }
                        else if (play_mode == 2)
                        {
                            modename = "[单曲循环]";
                        }
                        else if (play_mode == 3)
                        {
                            modename = "[顺序循环]";
                        }
                        else if (play_mode == 4)
                        {
                            modename = "[随机]";
                        }
                        else if (play_mode == 5)
                        {
                            modename = "[顺序销毁]";
                        }
                        else if (play_mode == 6)
                        {
                            modename = "[随机销毁]";
                        }
                    }
                    else
                    {
                        modename = "[FM]";
                    }
                    string comefrom = music_type == 0 ? "网易云音乐" : music_type == 1 ? "QQ音乐" : "未知";                    
                    newname = $" {songname}-{authorname}{modename}";
                    if (newname.Length >= 21)
                    {// 确保名字21以下
                        newname = newname.Substring(0, 21) + "...";
                    }
                    try
                    {
                        if (newname != botname_connect_before)
                        {// 相同则不换
                            await MainCommands.CommandBotName(ts3Client, newname);
                            botname_connect_before = newname;
                        
                    }
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"在更换机器人名字出现错误，目标名字: {newname}，错误: {e.Message}");
                    }

                    if (play_type == 0)
                    {
                        // 通知
                        await ts3Client.SendChannelMessage($"{comefrom}:播放歌曲{songname} - {authorname}, 第{play_index + 1}首 共{PlayList.Count}首 \n链接：{native_url}");
                    }
                    else
                    {
                        // 通知
                        await ts3Client.SendChannelMessage($"播放歌曲{songname} - {authorname}（{comefrom}FM）");
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            await MainCommands.CommandBotAvatarSet(ts3Client, picurl);
                            break;
                        }
                        catch (Exception)
                        {
                            await MainCommands.CommandBotAvatarSet(ts3Client, "");
                            await Task.Delay(1000);
                            if (i == 2) { await ts3Client.SendChannelMessage($"机器人头像更换失败,url: {picurl}"); }
                        }
                    }
                }
            }
        }
        //--------------------------映射字典------------------------
        // QQ电台ID与名称的映射字典，用于验证和显示
        private static readonly Dictionary<string, string> QqFmStations = new Dictionary<string, string>
{
    // 热门
    {"99", "猜你喜欢"}, {"101", "随心听"}, {"567", "抖音神曲"}, {"686", "深度催眠"},
    {"673", "情感治愈站"}, {"270", "KTV必点歌"}, {"127", "经典"}, {"167", "网络流行"},
    {"703", "宝宝胎教"}, {"215", "DJ舞曲"}, {"682", "助眠白噪音"}, {"199", "热歌"},
    {"307", "精选招牌歌"}, {"119", "粤语"}, {"136", "忧伤"}, {"568", "热门翻唱"},
    {"100", "跑步模式"}, {"269", "健身"}, {"447", "B榜热单"}, {"211", "平静"},
    {"554", "影视原声"}, {"347", "车载"}, {"322", "起床"}, {"550", "情歌"}, {"368", "睡前"},
    // 心情
    {"140", "快乐"}, {"572", "兴奋"}, {"137", "寂寞"}, {"217", "治愈"},
    // 主题
    {"126", "新歌"}, {"448", "独立唱作人"}, {"552", "游戏"}, {"570", "LIVE现场"},
    {"569", "综艺"}, {"610", "国漫原声"}, {"611", "经典日漫"}, {"612", "日漫新番"},
    // 场景
    {"702", "安抚哄睡"}, {"314", "门店"}, {"192", "旅行"}, {"335", "夜店"},
    {"325", "雨天"}, {"141", "咖啡馆"}, {"317", "学习"}, {"318", "工作"},
    // 曲风
    {"444", "欧美流行"}, {"134", "电音"}, {"345", "流行"}, {"365", "古风"},
    {"346", "民谣"}, {"129", "纯音乐"}, {"223", "Hip-Hop"}, {"190", "中国风"},
    {"173", "R&B"}, {"364", "民歌"}, {"130", "摇滚"}, {"133", "乡村"},
    {"523", "华语嘻哈"}, {"195", "舞曲"}, {"132", "爵士"}, {"131", "古典"},
    {"613", "民族音乐"}, {"602", "金属"}, {"604", "草原风情"}, {"605", "热门网络说唱"},
    {"606", "高原天籁"}, {"607", "西域风情"}, {"608", "经典雷鬼"}, {"609", "MC喊麦"},
    // 语言
    {"120", "英语"}, {"118", "国语"}, {"150", "韩语"}, {"149", "日语"},
    // 人群
    {"341", "儿童"}, {"123", "80后"}, {"124", "90后"}, {"298", "00后"}, {"122", "70后"},
    // 乐器
    {"174", "钢琴"}, {"175", "吉他"}, {"176", "小提琴"}, {"207", "古筝"},
    {"225", "笛子"}, {"181", "萨克斯"},
    // 陪你听
    {"584", "音乐故事"},
    // 厂牌
    {"663", "华纳音乐"}, {"558", "滚石唱片"}, {"571", "华研音乐"}, {"430", "SMTOWN"}
};
        //--------------------------qq输入------------------------
        private async Task ProcessQqInput(string arguments, Ts3Client ts3Client, bool isInsert)
        {
            string mid = "";
            string inputType = "未知"; // 用于日志和消息

            try
            {
                // --- 逻辑判断 ---
                // 1. 判断是否为 mid (以00开头)
                if (arguments.StartsWith("00") && arguments.Length > 10)
                {
                    inputType = "MID";
                    mid = arguments;
                }
                // 2. 判断是否为纯数字 id
                else if (Regex.IsMatch(arguments, @"^\d+$"))
                {
                    inputType = "数字ID";
                    //        await ts3Client.SendChannelMessage($"输入为 {inputType}，正在转换为 MID...");
                    mid = await musicapi.GetMidFromIdApi(arguments);
                    //        await ts3Client.SendChannelMessage($"转换成功，得到 MID: {mid}");
                }
                // 3. 判断是否为分享短链
                else if (arguments.Contains("c6.y.qq.com/base/fcgi-bin/u?__="))
                {
                    inputType = "分享短链";
                    //      await ts3Client.SendChannelMessage($"输入为 {inputType}，正在解析跳转...");
                    // 调用新方法解析短链获取数字ID
                    string songId = await musicapi.ResolveShortLinkAndGetId(arguments);

                    //  await ts3Client.SendChannelMessage($"解析成功，得到数字ID: {songId}。正在转换为 MID...");
                    // 使用数字ID调用API获取MID
                    mid = await musicapi.GetMidFromIdApi(songId);
                    //    await ts3Client.SendChannelMessage($"转换成功，得到 MID: {mid}");
                }
                // 4. 判断是否为方括号搜索或默认搜索
                else
                {
                    string keyword = arguments;
                    if (arguments.StartsWith("【") && arguments.EndsWith("】"))
                    {
                        inputType = "关键词搜索 (强制)";
                        keyword = arguments.Trim('【', '】');
                    }
                    else
                    {
                        inputType = "关键词搜索";
                    }

                    //await ts3Client.SendChannelMessage($"输入为 {inputType}，正在搜索: {keyword}...");
                    Dictionary<string, string> searchResult = await musicapi.SearchSong(keyword, 1);
                    if (searchResult != null && searchResult.ContainsKey("id"))
                    {
                        mid = searchResult["id"];
                        string songname_get = searchResult["name"];
                        string author_get = searchResult["author"];
                        await ts3Client.SendChannelMessage($"搜索到歌曲: {songname_get} - {author_get} (MID: {mid})");
                    }
                    else
                    {
                        await ts3Client.SendChannelMessage($"未能搜索到与“{keyword}”相关的歌曲。");
                        return;
                    }
                }

                // --- 执行添加操作 ---
                if (!string.IsNullOrEmpty(mid))
                {
                    int insertIndex = isInsert ? play_index + 1 : -1;
                    await PlayListAdd(mid, 1, insertIndex);

                    
                }
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"处理QQ音乐请求时出错 ({inputType}): {e.Message}");
            }
        }
        //--------------------------专辑指令段--------------------------
        private async Task ProcessAndPlayAlbum(string arguments, int music_type)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("已切换至普通播放模式");
            }

            try
            {
                isObstruct = true;
                await ts3Client.SendChannelMessage("开始获取专辑信息...");

                string albumId = "";
                string searchKeyword = arguments;
                bool isIdSearch = false;

                // --- 全新的方括号逻辑 ---
                if (arguments.StartsWith("【") && arguments.EndsWith("】"))
                {
                    // 如果用户输入了方括号，强制视为专辑名搜索
                    searchKeyword = arguments.Trim('【', '】');
                    isIdSearch = false;
                }
                else
                {
                    // 简单的ID判断：纯数字认为是网易云ID，字母数字组合认为是QQ音乐ID
                    Regex pureNumRgx = new Regex(@"^\d+$");
                    Regex alnumRgx = new Regex("^[a-zA-Z0-9]+$");

                    if ((music_type == 0 && pureNumRgx.IsMatch(arguments)) || (music_type == 1 && alnumRgx.IsMatch(arguments) && !pureNumRgx.IsMatch(arguments)))
                    {
                        albumId = arguments;
                        isIdSearch = true;
                    }
                }
                // --- 后续逻辑 ---
                if (!isIdSearch)
                    {
                        // 如果不是ID，进行搜索
                        albumId = await musicapi.SearchAlbum(searchKeyword, music_type);
                        if (string.IsNullOrEmpty(albumId))
                        {
                            await ts3Client.SendChannelMessage($"未能找到名为《{searchKeyword}》的专辑。");
                            isObstruct = false;
                            return;
                        }
                    }

                    // 获取专辑详情和歌曲列表
                var albumDetails = await musicapi.GetAlbumDetail(albumId, music_type);
                if (albumDetails == null || albumDetails.Item2.Count == 0)
                {
                    await ts3Client.SendChannelMessage("无法获取专辑详情或专辑内没有歌曲。");
                    isObstruct = false;
                    return;
                }

                string albumName = albumDetails.Item1;
                var songs = albumDetails.Item2;                

                PlayList.Clear();

                // 批量将歌曲添加到播放列表
                foreach (var song in songs)
                {
                    PlayList.Add(new Dictionary<string, string> {
                { "id", song["id"] },
                { "music_type", music_type.ToString() },
                { "name", song["name"] },
                { "author", song["author"] }
            });
                }

                await ts3Client.SendChannelMessage($"专辑《{albumName}》添加完成，共 {songs.Count} 首歌曲！即将播放...");
              



                play_index = 0;
                await PlayListPlayNow();
                
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"处理专辑时出错: {e.Message}");
            }
            finally
            {
                isObstruct = false;
            }
        }
        //--------------------------指令段--------------------------
        [Command("test")]
        public async Task CommandTest(PlayManager playManager, Player player, Ts3Client ts3Client, ConfBot confbot)
        {
            Console.WriteLine("来测！");
            //Dictionary<string,string> detail = new Dictionary<string,string>();
            // await ts3Client.SendChannelMessage(musicapi.cookies[0]);
            // await ts3Client.SendChannelMessage(musicapi.cookies[1]);
            // string res = await musicapi.GetSongUrl("002IUAKS0hutBn", 1);
            // await ts3Client.SendChannelMessage(res);
            // await MainCommands.CommandBotName(ts3Client, "123"); //  修改名字
            // await ts3Client.SendChannelMessage($"{player.Position}");
            // await MainCommands.CommandBotAvatarSet(ts3Client, "https://p1.music.126.net/tMQXBMTy8pGjGggX1j0YNQ==/109951169389595068.jpg");
            // await MainCommands.CommandBotAvatarSet(ts3Client, "https://p2.music.126.net/ZR6QuByWgej9-aRhZjLqHw==/109951163803188844.jpg");
            // await ts3Client.ChangeDescription("");
            // List<Dictionary<TimeSpan, string>> lyric = await musicapi.GetSongLyric("28285910", 0);
            //  await ts3Client.SendChannelMessage($"{Lyric[0].First().Key} = {Lyric[0][Lyric[0].First().Key]}");
            // await ts3Client.SendChannelMessage($"{Lyric[5].First().Key} = {Lyric[5][Lyric[5].First().Key]}");
            // List<Dictionary<TimeSpan, string>> lyric = await musicapi.GetSongLyric("004MdHVz0vDB5C", 1);
            // await ts3Client.SendChannelMessage($"{Lyric.Count}");
            // await ts3Client.SendChannelMessage($"{lyric[0].First().Key} = {lyric[0][lyric[0].First().Key]}");
            // await ts3Client.SendChannelMessage($"{lyric[5].First().Key} = {lyric[5][lyric[5].First().Key]}");
            // isLyric = false;
            // await ts3Client.SendChannelMessage(flag_i.ToString());
            // string share = await musicapi.ResolveShortLinkAndGetId("https://c6.y.qq.com/base/fcgi-bin/u?__=WlynoIQMRiEy");
            //  Console.WriteLine(share);
            string res = await musicapi.GetMidFromIdApi("339618634");
              Console.WriteLine(res);
        }
        [Command("wq play")]
        public async Task CommandMusicPlay(string argments, Ts3Client ts3Client)
        {
            await CommandWyyPlay(argments, ts3Client);
        }
        [Command("wq seek")]
        public async Task CommandSeek(string argments)
        {
            long value = 0;
            if (long.TryParse(argments, out value))
            {
                if (!player.Paused && playManager.IsPlaying)
                {
                    StartLyric(false);
                    long p_now, p_length;
                    p_now = (long)((TimeSpan)player.Position).TotalSeconds;
                    p_length = (long)((TimeSpan)player.Length).TotalSeconds;
                    if (value < p_length && value >= 0)
                    {
                        await player.Seek(TimeSpan.FromSeconds(value));
                    }
                    // 延迟1000防止启动线程的时候seek还没完成
                    await Task.Delay(1000);
                    StartLyric(true);
                }
            }
        }
        [Command("wq next")]
        public async Task CommandNext(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            await PlayListNext();
        }
        [Command("wq pre")]
        public async Task CommandPre(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            await PlayListPre();
        }
        [Command("wq mode")]
        public async Task CommandMode(int argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (1 <= argments && argments <= 6)
            {
                if (play_type == 0)
                {
                    play_mode = argments;
                    string notice = "";
                    switch (play_mode)
                    {
                        case 1: notice = "顺序播放模式"; break;
                        case 2: notice = "单曲循环模式"; break;
                        case 3: notice = "顺序循环模式"; break;
                        case 4: notice = "随机播放模式"; break;
                        case 5: notice = "顺序销毁模式"; break;
                        case 6: notice = "随机销毁模式"; break;
                        default: break;
                    }
                    await ts3Client.SendChannelMessage(notice);
                }
                else if (play_type == 1)
                {
                    await ts3Client.SendChannelMessage("处于FM模式, 无法切换播放模式");
                }
            }
            else
            {
                await ts3Client.SendChannelMessage("输入参数错误");
            }
        }
        [Command("wq ls")]
        public async Task CommandLs()
        {
            await PlayListShow(play_index / 10 + 1);
        }
        [Command("wq ls p")]
        public async Task CommandLsPage(int page, InvokerData invokerData)
        {
            // 展示第page页
            await PlayListShow(page);
        }
        [Command("wq go")]       
        public async Task CommandGo(Ts3Client ts3Client, int? argments = null)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }

            // --- 情况1: 用户输入了 "!bgm go [编号]" (跳转) ---
            if (argments.HasValue)
            {
                int targetIndex = argments.Value;
                if (0 < targetIndex && targetIndex <= PlayList.Count)
                {
                    play_index = targetIndex - 1;
                    await ts3Client.SendChannelMessage($"已跳转到网易/QQ列表第{targetIndex}首歌。");
                    await PlayListPlayNow();
                }
                else
                {
                    await ts3Client.SendChannelMessage($"超出索引范围, 范围[1,{PlayList.Count}]");
                }
            }
            // --- 情况2: 用户只输入了 "!bgm go" (重播或播放) ---
            else
            {
                // a) 如果有歌曲正在播放，则重播当前歌曲
                if (playManager.IsPlaying)
                {
                    await ts3Client.SendChannelMessage($"已跳转到网易/QQ列表第{play_index+1}首歌。");
                    await PlayListPlayNow();
                }
                // b) 如果当前没有歌曲播放，但播放列表不为空
                else if (PlayList.Count > 0)
                {
                    await ts3Client.SendChannelMessage("开始播放网易/QQ列表。");
                    play_index = 0; // 从第一首开始
                    await PlayListPlayNow();
                }
                // c) 如果播放列表是空的
                else
                {
                    await ts3Client.SendChannelMessage("播放列表是空的，没有可以播放的歌曲。");
                }
            }
        }
        [Command("wq move")]
        public async Task CommandMv(int idx, int target, Ts3Client ts3Client)
        {// idx为要移动的歌曲, target为目标, 范围是[1,PlayList.Count], 若target为
            if (idx < 1 || idx > PlayList.Count)
            {
                await ts3Client.SendChannelMessage($"idx: {idx}超出索引, 范围[1,{PlayList.Count}]");
                return;
            }
            if (target < 1 || target > PlayList.Count)
            {
                await ts3Client.SendChannelMessage($"target: {target}超出索引, 范围[1,{PlayList.Count}]");
                return;
            }
            if (idx == target)
            {
                await ts3Client.SendChannelMessage($"自己移自己");
                return;
            }
            int idx0 = idx - 1;
            int target0 = target - 1;
            // 保存当前播放的歌曲索引
            int oldPlayIndex = play_index;
            // 如果移动的是当前播放的歌曲
            if (idx0 == play_index)
            {
                // 更新play_index
                play_index = target0;
            }
            // 如果移动的不是当前播放的歌曲，但移动操作影响了play_index的位置
            else
            {
                if (idx0 < play_index)
                {
                    if (target0 > play_index)
                    {
                        play_index--;
                    }
                }
                else if (idx0 > play_index)
                {
                    if (target0 < play_index)
                    {
                        play_index++;
                    }
                }
            }
            // 移动元素
            Dictionary<string, string> item = PlayList[idx0];
            PlayList.RemoveAt(idx0);
            PlayList.Insert(target0, item);
            // 发送消息
            await ts3Client.SendChannelMessage($"已将歌曲从位置 {idx} 移动到位置 {target}, 当前播放位置：{play_index + 1}");
            await PlayListShow((target - 1) / 10 + 1);
        }
        [Command("wq remove")]
        public async Task CommandRm(int argments, Ts3Client ts3Client, Player player)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 歌单删除
            if (1 <= argments && argments <= PlayList.Count)
            {
                string name = "";
                PlayList[argments - 1].TryGetValue("name", out name);
                await ts3Client.SendChannelMessage($"删除歌曲{name}");
                PlayList.Remove(PlayList[argments - 1]);

                if (play_index == argments - 1)
                {
                    // 删除正在播放的歌曲
                    if (play_index < PlayList.Count)
                    {
                        if (!player.Paused || !playManager.IsPlaying)
                        {// 正在播放
                            await PlayListPlayNow();
                        }
                    }
                    else
                    {
                        play_index = 0;
                        if (!player.Paused || !playManager.IsPlaying)
                        {// 正在播放
                            if (PlayList.Count > 0)
                            {// 列表有歌曲
                                await PlayListPlayNow();
                            }
                            else
                            {
                                MainCommands.CommandPause(player);
                                MainCommands.CommandStop(playManager);
                            }
                        }
                    }
                }
                else if (play_index < argments - 1)
                {
                    play_index--;
                }
            }
            else
            {
                await ts3Client.SendChannelMessage($"超出索引范围, 范围[1,{PlayList.Count}]");
            }
        }
        [Command("wq clear")]
        public async Task CommandClearList(Ts3Client ts3Client, Player player)
        {
            PlayList.Clear();
            play_index = 0;
            lyric_before = "";
            await ts3Client.SendChannelMessage($"歌单清空");
            lyric_id_now = "";
            if (!player.Paused || !playManager.IsPlaying)
            {
                MainCommands.CommandPause(player);
                MainCommands.CommandStop(playManager);
            }
            // 在这里重置标志位
            isPlayingNeteaseOrQQ = false;
           
            if (botname_connect_before != botname_connect)
            {
                await ts3Client.ChangeName(botname_connect);
                botname_connect_before = botname_connect;
            }
            await ts3Client.ChangeDescription("");
            await ts3Client.DeleteAvatar();
        }
        [Command("wq status")]
        public async Task<string> CommandStatus()
        {
            StringBuilder result = new StringBuilder();
            result.Append("登录状态："); // 第1行，AppendLine 会自动在末尾加上换行
            // Netease Status
            result.Append($"\n\n[网易云音乐]\nAPI 地址：{musicapi.GetNeteaseApiServerUrl()}\n当前用户：");
            try
            {
                var neteaseUser = await musicapi.GetNeteaseUserInfo();
                if (neteaseUser == null)
                {
                    result.Append("未登录\n");
                }
                else
                {
                    result.Append($"{neteaseUser.Name}[{neteaseUser.Url}]");
                    result.Append($"\n会员状态：{neteaseUser.Extra}\n");
                }
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"获取网易云音乐用户信息时出错：{e.Message}");
                result.Append($"[color=red]获取失败：{e.Message}[/color]\n");
                Console.WriteLine($"[Netease QQ Plugin Error] GetNeteaseUserInfo failed: {e}");
            }

            // QQ Music Status
            result.Append($"\n[QQ音乐]\nAPI 地址：{musicapi.GetQQApiServerUrl()}\n当前用户：");
            try
            {
                var qqUser = await musicapi.GetQQUserInfo();
                if (qqUser == null)
                {
                    result.Append("未登录\n");
                }
                else
                {
                    result.Append($"{qqUser.Name}[{qqUser.Url}]");

                    result.Append($"\n会员状态：{qqUser.Extra}");

                    result.Append("\n");
                }
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"获取QQ音乐用户信息时出错: {e.Message}");
                result.Append($"[color=red]获取失败: {e.Message}[/color]\n");
                Console.WriteLine($"[Netease QQ Plugin Error] GetQQUserInfo failed: {e}");
            }

            return result.ToString();
        }
        //--------------------------歌词指令段--------------------------
        [Command("wq lyric")]
        public async Task CommandLyric(Ts3Client ts3Client)
        {
            // 通过用户的指令来创建歌词线程
            if (isLyric)
            {
                isLyric = false;
                await ts3Client.SendChannelMessage("歌词已关闭");
                await ts3Client.ChangeDescription("");
                StartLyric(false);
            }
            else
            {
                StartLyric(true);
                await ts3Client.SendChannelMessage("歌词已开启");
                isLyric = true;
            }
        }



        //--------------------------网易云指令段--------------------------
        [Command("wyy login")]
        public async Task CommandWyyLogin(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            try
            {
                isObstruct = true;
                // 网易云音乐登录
                await ts3Client.SendChannelMessage("正在登录...");
                await MainCommands.CommandBotDescriptionSet(ts3Client, "扫码登录");
                // 把二维码发送ts3bot
                Stream img_stream = await musicapi.GetNeteaseLoginImage();
                await ts3Client.SendChannelMessage(musicapi.netease_login_key);
                await ts3Client.UploadAvatar(img_stream);
                int trytime = 60;
                for (int i = 0; i < trytime; i++)
                {
                    Thread.Sleep(2000);
                    Dictionary<string, string> res = await musicapi.CheckNeteaseLoginStatus();
                    if (res["code"] == "803")
                    {
                        _ = ts3Client.SendChannelMessage("登录成功");
                        // 保存cookies
                        string cookies = "\"" + res["cookie"] + "\"";
                        musicapi.SetCookies(res["cookie"], 0);
                        plugin_config["netease"]["cookies"] = cookies;
                        plugin_config_parser.WriteFile(iniPath, plugin_config);

                        break;
                    }
                    else if (res["code"] == "800")
                    {
                        _ = ts3Client.SendChannelMessage(res["message"]);
                        break;
                    }
                    else
                    {
                        _ = ts3Client.SendChannelMessage(res["message"]);
                    }
                    if (res["code"] != "803" && i == trytime - 1)
                    {
                        _ = ts3Client.SendChannelMessage("登录超时");
                        break;
                    }
                }
                isObstruct = false;
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                isObstruct = false;
            }

        }
        [Command("wyy play")]
        public async Task CommandWyyPlay(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 单独音乐播放
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            if (PlayList.Count == 0)
            {
                await CommandWyyAdd(argments, ts3Client);
            }
            else
            {                
                await CommandWyyInsert(argments, ts3Client);
                await PlayListNext();
            }
        }
        [Command("wyy insert")]
        public async Task CommandWyyInsert(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 单独音乐播放
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            if (PlayList.Count == 0)
            {
                await CommandWyyAdd(argments, ts3Client);
            }
            else
            {
                long id = 0;
                string songname = "";
                if (long.TryParse(argments, out id))
                {
                    // 输入为id
                    await PlayListAdd(id.ToString(), 0, play_index + 1);
                }
                else
                {
                    // 输入为歌名
                    songname = argments;
                    Dictionary<string, string> res = await musicapi.SearchSong(songname, 0);
                    string songname_get;
                    string author_get;
                    songname_get = res["name"];
                    author_get = res["author"];
                    long.TryParse(res["id"], out id);
                    await ts3Client.SendChannelMessage($"搜索歌\"{songname}\"得到\"{songname_get}-{author_get}\", id\"{id}\"");
                    await PlayListAdd(id.ToString(), 0, play_index + 1);
                }
            }
        }
        [Command("wyy add")]
        public async Task CommandWyyAdd(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            long id = 0;
            string songname = "";
            if (long.TryParse(argments, out id))
            {

                // 输入为id
                await PlayListAdd(id.ToString(), 0);
            }
            else
            {
                // 输入为歌名
                songname = argments;
                Dictionary<string, string> res = await musicapi.SearchSong(songname, 0);
                string songname_get;
                string author_get;
                songname_get = res["name"];
                author_get = res["author"];
                long.TryParse(res["id"], out id);
                await ts3Client.SendChannelMessage($"搜索歌\"{songname}\"得到\"{songname_get}-{author_get}\", id\"{id}\"");
                await PlayListAdd(id.ToString(), 0);
            }
        }
        [Command("wyy agd")]
        public async Task CommandWyyAGd(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 添加歌单
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            try
            {
                await ts3Client.SendChannelMessage("开始获取歌单");
                isObstruct = true;
                long id_gd = 0;
                string searchKeyword = argments;
                bool isIdSearch = false;

                // --- 全新的方括号逻辑 ---
                if (argments.StartsWith("【") && argments.EndsWith("】"))
                {
                    searchKeyword = argments.Trim('【', '】');
                    isIdSearch = false;
                }
                else if (long.TryParse(argments, out id_gd))
                {
                    isIdSearch = true;
                }

                if (!isIdSearch)
                {
                    // 输入为歌名
                    id_gd = await musicapi.SearchPlayList(searchKeyword, 0);
                    if (id_gd == 0)
                    {
                        await ts3Client.SendChannelMessage($"未能找到名为《{searchKeyword}》的歌单。");
                        isObstruct = false;
                        return;
                    }
                }

                List<Dictionary<string, string>> detail_playlist = await musicapi.GetPlayListDetail(id_gd.ToString(), 0);
                // 添加歌单
                for (int i = 0; i < detail_playlist.Count; i++)
                {
                    string song_id = detail_playlist[i].TryGetValue("id", out song_id) ? song_id : "";
                    string song_name = detail_playlist[i].TryGetValue("name", out song_name) ? song_name : "";
                    string song_author = detail_playlist[i].TryGetValue("author", out song_author) ? song_author : "";
                    PlayList.Add(new Dictionary<string, string> {
                    {"id", song_id},
                    { "music_type", "0" },
                    { "name", song_name },
                    {"author", song_author }
                });
                }
                isObstruct = false;
                await ts3Client.SendChannelMessage($"导入歌单完成, 歌单共{detail_playlist.Count}首歌, 现在列表一共{PlayList.Count}首歌");
                if (player.Paused || !playManager.IsPlaying)
                {
                    await PlayListPlayNow();
                }
            }
            catch (Exception)
            {
                isObstruct = false;
                throw;
            }
        }
        [Command("wyy gd")]
        public async Task CommandWyyGd(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 添加歌单
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            try
            {
                await ts3Client.SendChannelMessage("开始获取歌单");
                isObstruct = true;
                long id_gd = 0;
                string searchKeyword = argments;
                bool isIdSearch = false;

                // --- 全新的方括号逻辑 ---
                if (argments.StartsWith("【") && argments.EndsWith("】"))
                {
                    searchKeyword = argments.Trim('【', '】');
                    isIdSearch = false;
                }
                else if (long.TryParse(argments, out id_gd))
                {
                    isIdSearch = true;
                }

                if (!isIdSearch)
                {
                    // 输入为歌名
                    id_gd = await musicapi.SearchPlayList(searchKeyword, 0);
                    if (id_gd == 0)
                    {
                        await ts3Client.SendChannelMessage($"未能找到名为《{searchKeyword}》的歌单。");
                        isObstruct = false;
                        return;
                    }
                }

                List<Dictionary<string, string>> detail_playlist = await musicapi.GetPlayListDetail(id_gd.ToString(), 0);
                // 添加歌单
                PlayList.Clear();
                for (int i = 0; i < detail_playlist.Count; i++)
                {
                    string song_id = detail_playlist[i].TryGetValue("id", out song_id) ? song_id : "";
                    string song_name = detail_playlist[i].TryGetValue("name", out song_name) ? song_name : "";
                    string song_author = detail_playlist[i].TryGetValue("author", out song_author) ? song_author : "";
                    PlayList.Add(new Dictionary<string, string> {
                    {"id", song_id},
                    { "music_type", "0" },
                    { "name", song_name },
                    {"author", song_author }
                });
                }
                isObstruct = false;
                await ts3Client.SendChannelMessage($"搜索完成, 歌单共{detail_playlist.Count}首歌, 现在列表一共{PlayList.Count}首歌");

                play_index = 0;
                await PlayListPlayNow();
            }
            catch (Exception)
            {
                isObstruct = false;
                throw;
            }
        }
        [Command("wyy fm")]
        public async Task CommandWyyFM(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 播放FM模式
            if (play_type != 1)
            {
                play_type = 1;
                await ts3Client.SendChannelMessage("切换至FM模式");
            }
            fm_platform = 0;
            await PlayFMNow();
        }
        [Command("wyy zj")]
        public async Task CommandWyyZj(string arguments)
        {
            await ProcessAndPlayAlbum(arguments, 0); // 0 代表网易云
        }


        //--------------------------QQ音乐指令段--------------------------
        [Command("qq login")]
        public async Task CommandQQLogin(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            try
            {
                isObstruct = true;
                // 扫码登录
                await ts3Client.SendChannelMessage("正在登录...");
                await MainCommands.CommandBotDescriptionSet(ts3Client, "扫码登录");
                Stream img_stream;
                try
                {
                    img_stream = await musicapi.GetQQLoginImage();
                }
                catch (Exception e)
                {
                    await ts3Client.SendChannelMessage($"在获取QQ登录二维码过程出现错误: {e.Message}");
                    return;
                }
                if (img_stream == null) { await ts3Client.SendChannelMessage("null"); return; }
                await ts3Client.UploadAvatar(img_stream);
                // 开始等待扫码
                int trytime = 15;
                int i = 0;
                for (i = 0; i < trytime; i++)
                {
                    Thread.Sleep(2000);
                    Dictionary<string, string> res = await musicapi.CheckQQLoginStatus();
                    if (res["isOK"] == "True")
                    {
                        await ts3Client.SendChannelMessage("登录成功");
                        // 保存cookies
                        await ts3Client.SendChannelMessage(res["uin"]);
                        await ts3Client.SendChannelMessage(res["cookie"]);

                        string cookies = "\"" + res["cookie"] + "\"";
                        musicapi.SetCookies(res["cookie"], 1);
                        plugin_config["qq"]["cookies"] = cookies;
                        plugin_config_parser.WriteFile(iniPath, plugin_config);
                        break;
                    }
                    else
                    {
                        await ts3Client.SendChannelMessage(res["message"]);
                    }
                }
                await ts3Client.SendChannelMessage("结束登录");
                isObstruct = false;
            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                isObstruct = false;
            }
        }
        [Command("qq cookie")]
        public async Task CommandQQCookie(string argments)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            argments = argments.Trim(new char[] { '"' });
            argments = string.IsNullOrEmpty(argments) ? "" : argments;

            string res = await musicapi.SetQQLoginCookies(argments);
            if (res == "true")
            {
                // 登录成功，保存cookie
                qqmusic_cookies = argments;
                string cookies = "\"" + argments + "\"";
                musicapi.SetCookies(cookies, 1);
                plugin_config["qq"]["cookies"] = cookies;
                plugin_config_parser.WriteFile(iniPath, plugin_config);
                await ts3Client.SendChannelMessage("QQ cookie 设置完成");
            }
            else
            {
                await ts3Client.SendChannelMessage(res);
            }
        }
        [Command("qq load")]
        public async Task CommandQQLoad(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 读取本地中的cookies，然后将他保存到qqmusic api
            plugin_config = plugin_config_parser.ReadFile(iniPath);
            qqmusic_cookies = plugin_config["qq"]["cookies"];
            qqmusic_cookies = qqmusic_cookies.Trim(new char[] { '"' });
            qqmusic_cookies = string.IsNullOrEmpty(qqmusic_cookies) ? "" : qqmusic_cookies;

            string res = await musicapi.SetQQLoginCookies(qqmusic_cookies);
            if (res == "true")
            {
                // 登录成功，保存cookie
                string cookies = "\"" + qqmusic_cookies + "\"";
                musicapi.SetCookies(cookies, 1);
                plugin_config["qq"]["cookies"] = cookies;
                plugin_config_parser.WriteFile(iniPath, plugin_config);
                await ts3Client.SendChannelMessage("QQ登录完成");
            }
            else
            {
                await ts3Client.SendChannelMessage(res);
            }
        }
        [Command("qq play")]
        public async Task CommandQQPlay(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 单独音乐播放
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            if (PlayList.Count == 0)
            {
                await CommandQQAdd(argments, ts3Client);
            }
            else
            {
                int index_before_insert = play_index;
                await ProcessQqInput(argments, ts3Client, isInsert: true);
                await PlayListNext();
            }
        }
        [Command("qq insert")]
        public async Task CommandQQInsert(string arguments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }

            if (PlayList.Count == 0)
            {
                // 如果列表为空，insert 和 add 行为一致
                await ProcessQqInput(arguments, ts3Client, isInsert: false);
            }
            else
            {
                await ProcessQqInput(arguments, ts3Client, isInsert: true);
            }
        }
        [Command("qq add")]
        public async Task CommandQQAdd(string arguments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }

            await ProcessQqInput(arguments, ts3Client, isInsert: false);
        }
        [Command("qq agd")]
        public async Task CommandQQAGd(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            await ts3Client.SendChannelMessage("开始获取歌单");
            isObstruct = true;
            long id_gd = 0;
    string searchKeyword = argments;
    bool isIdSearch = false;

    // --- 全新的方括号逻辑 ---
    if (argments.StartsWith("【") && argments.EndsWith("】"))
    {
        searchKeyword = argments.Trim('【', '】');
        isIdSearch = false;
    }
    else if (long.TryParse(argments, out id_gd))
    {
        isIdSearch = true;
    }

    try
    {
        if (!isIdSearch)
        {
            // 输入为歌名
            id_gd = await musicapi.SearchPlayList(searchKeyword, 1);
             if (id_gd == 0) {
                await ts3Client.SendChannelMessage($"未能找到名为《{searchKeyword}》的歌单。");
                isObstruct = false;
                return;
            }
        }
                List<Dictionary<string, string>> detail_playlist = await musicapi.GetPlayListDetail(id_gd.ToString(), 1);
                for (int i = 0; i < detail_playlist.Count; i++)
                {
                    string song_id = detail_playlist[i].TryGetValue("id", out song_id) ? song_id : "";
                    string song_name = detail_playlist[i].TryGetValue("name", out song_name) ? song_name : "";
                    string song_author = detail_playlist[i].TryGetValue("author", out song_author) ? song_author : "";
                    PlayList.Add(new Dictionary<string, string> {
                     {"id", song_id},
                     { "music_type", "1" },
                     { "name", song_name },
                     {"author", song_author }
                 });
                }
                isObstruct = false;
                await ts3Client.SendChannelMessage($"导入歌单完成, 歌单共{detail_playlist.Count}首歌, 现在列表一共{PlayList.Count}首歌");
                if (player.Paused || !playManager.IsPlaying)
                {
                    await PlayListPlayNow();
                }
            }
            catch (Exception)
            {
                isObstruct = false;
                throw;
            }
        }
        [Command("qq gd")]
        public async Task CommandQQGd(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            await ts3Client.SendChannelMessage("开始获取歌单");
            isObstruct = true;
            long id_gd = 0;
            string searchKeyword = argments;
            bool isIdSearch = false;

            // --- 全新的方括号逻辑 ---
            if (argments.StartsWith("【") && argments.EndsWith("】"))
            {
                searchKeyword = argments.Trim('【', '】');
                isIdSearch = false;
            }
            else if (long.TryParse(argments, out id_gd))
            {
                isIdSearch = true;
            }

            try
            {
                if (!isIdSearch)
                {
                    // 输入为歌名
                    id_gd = await musicapi.SearchPlayList(searchKeyword, 1);
                    if (id_gd == 0)
                    {
                        await ts3Client.SendChannelMessage($"未能找到名为《{searchKeyword}》的歌单。");
                        isObstruct = false;
                        return;
                    }
                }
                PlayList.Clear();
                List<Dictionary<string, string>> detail_playlist = await musicapi.GetPlayListDetail(id_gd.ToString(), 1);
                for (int i = 0; i < detail_playlist.Count; i++)
                {
                    string song_id = detail_playlist[i].TryGetValue("id", out song_id) ? song_id : "";
                    string song_name = detail_playlist[i].TryGetValue("name", out song_name) ? song_name : "";
                    string song_author = detail_playlist[i].TryGetValue("author", out song_author) ? song_author : "";
                    PlayList.Add(new Dictionary<string, string> {
                     {"id", song_id},
                     { "music_type", "1" },
                     { "name", song_name },
                     {"author", song_author }
                 });
                }
                isObstruct = false;
                await ts3Client.SendChannelMessage($"搜索完成, 歌单共{detail_playlist.Count}首歌, 现在列表一共{PlayList.Count}首歌");
                if (playManager.IsPlaying)
                {
                    MainCommands.CommandPause(player);
                    await Task.Delay(200);
                }

                play_index = 0;
                await PlayListPlayNow();
            }
            catch (Exception)
            {
                isObstruct = false;
                throw;
            }
        }
        [Command("qq zj")]
        public async Task CommandQqZj(string arguments)
        {
            await ProcessAndPlayAlbum(arguments, 1); // 1 代表QQ音乐
        }     
        [Command("qq fm")]
        public async Task CommandQqFM(Ts3Client ts3Client, string radioId = null)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }

            try
            {
                isObstruct = true;

                // --- 新增逻辑：处理和保存新的电台ID ---
                if (!string.IsNullOrEmpty(radioId))
                {
                    // 验证用户输入的ID是否存在于我们的字典中
                    if (QqFmStations.ContainsKey(radioId))
                    {
                        // 更新当前要播放的电台ID
                        qqmusic_fm_id = radioId;

                        // 将新的ID保存到配置文件
                        plugin_config["qq"]["qqfm"] = qqmusic_fm_id;
                        plugin_config_parser.WriteFile(iniPath, plugin_config);

                        await ts3Client.SendChannelMessage($"QQ电台已切换为: {QqFmStations[radioId]} (ID: {radioId})。");
                    }
                    else
                    {
                        await ts3Client.SendChannelMessage($"无效的电台ID: '{radioId}'。请使用 !qq fm ls 查看所有可用电台。");
                        isObstruct = false;
                        return;
                    }
                }
                // --- 新增逻辑结束 ---

                // 切换到FM播放模式
                if (play_type != 1)
                {
                    play_type = 1;
                    await ts3Client.SendChannelMessage("已切换至FM模式");
                }
                fm_platform = 1;
                await PlayFMNow();
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage($"播放QQ电台时出错: {e.Message}");
            }
            finally
            {
                isObstruct = false;
            }
        }
        [Command("qq fmls")]
        public async Task CommandQqFmList(Ts3Client ts3Client)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("所有可用的QQ电台列表如下 (使用 !qq fm [ID] 来切换):");

            // 为了美观，进行简单的分组
            var categories = new Dictionary<string, string[]>
    {
        { "热门", new[]{"99", "101", "567", "686", "673", "270", "127", "167", "703", "215", "682", "199", "307", "119", "136", "568", "100", "269", "447", "211", "554", "347", "322", "550", "368"} },
        { "心情", new[]{"136", "140", "211", "572", "137", "217"} },
        { "主题", new[]{"199", "126", "550", "554", "347", "127", "447", "448", "567", "552", "215", "570", "568", "569", "610", "611", "612", "167", "270", "307"} },
        { "场景", new[]{"368", "682", "322", "703", "269", "702", "314", "192", "335", "325", "141", "317", "318"} },
        { "曲风", new[]{"444", "134", "345", "365", "346", "129", "223", "190", "173", "364", "130", "133", "523", "195", "132", "131", "613", "602", "604", "605", "606", "607", "608", "609"} },
        { "语言", new[]{"120", "118", "119", "150", "149"} },
        { "人群", new[]{"341", "123", "124", "298", "122"} },
        { "乐器", new[]{"174", "175", "176", "207", "225", "181"} },
        { "陪你听", new[]{"346", "584", "365", "134", "174", "686", "550", "129", "702", "703"} },
        { "厂牌", new[]{"663", "558", "571", "430"} }
    };

            foreach (var category in categories)
            {
                sb.AppendLine($"\n--- {category.Key} ---");
                List<string> lineEntries = new List<string>();
                foreach (var id in category.Value)
                {
                    if (QqFmStations.TryGetValue(id, out var name))
                    {
                        lineEntries.Add($"{id}:{name}");
                    }
                }
                // 每行显示3个，避免刷屏
                for (int i = 0; i < lineEntries.Count; i += 3)
                {
                    sb.AppendLine(string.Join(" | ", lineEntries.Skip(i).Take(3)));
                }
            }

            await ts3Client.SendChannelMessage(sb.ToString());
        }

        //--------------------------事件--------------------------
        private async Task OnSongStop(object sender, EventArgs e)
        {
            if (!isPlayingNeteaseOrQQ)
            {
                return;
            }

            if (play_type == 0 && play_mode == 2)
            {
                // 单曲循环
                await PlayListPlayNow();
            }
            else
            {
                // 下一首
                Console.WriteLine("歌曲播放结束");
                await PlayListNext();
            }
        }
        private Task AfterSongStart(object sender, PlayInfoEventArgs value)
        {
                // Console.WriteLine("NeteaseQQPlugin:放放放！");

            if (value.ResourceData?.Get("source") != "NeteaseQQPlugin")
            {
                isPlayingNeteaseOrQQ = false;
                // （可选）你可以在这里加一条日志，方便调试
                // Console.WriteLine("NeteaseQQPlugin:我没在干.");
            }
            else
            {
                // Console.WriteLine("NeteaseQQPlugin: 是我在干.");
            }

            this.invokerData = value.Invoker;
            return Task.CompletedTask;
        }
    



        private async Task OnAlone(object sender, EventArgs e)
        {
            var args = e as AloneChanged;
            if (args != null)
            {
                if (args.Alone)
                {// 频道无人
                    if (!player.Paused || !playManager.IsPlaying)
                    {
                        waiting_time = 1;
                        while (waiting_time <= max_wait_alone && waiting_time != 0)
                        {
                            waiting_time++;
                            await Task.Delay(1000);
                            if (waiting_time >= max_wait_alone)
                            {// 等待三十秒
                                MainCommands.CommandPause(player);
                                await ts3Client.SendServerMessage("频道无人，已自动暂停音乐");
                                waiting_time = 0;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    waiting_time = 0;
                }
            }
        }
        private async Task OnTest(object sender, EventArgs e)
        {
            await ts3Client.SendChannelMessage("get Test");
            // await ts3Client.ChangeDescription("aabbbabababababa");
        }



        public void Dispose()
        {
            // Don't forget to unregister everything you have subscribed to,
            // otherwise your plugin will remain in a zombie state
            plugin_config_parser.WriteFile(iniPath, plugin_config);
            playManager.PlaybackStopped -= OnSongStop;
            ts3Client.OnAloneChanged -= OnAlone;
            playManager.AfterResourceStarted -= AfterSongStart;
            if (isLyric == true)
            {
                isLyric = false;
            }
            if (Lyric_thread != null)
            {
                Lyric_thread.Close();
            }
        }

    }
}
