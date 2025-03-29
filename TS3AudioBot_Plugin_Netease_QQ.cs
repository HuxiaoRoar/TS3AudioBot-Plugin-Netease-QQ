using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TS3AudioBot;
using TS3AudioBot.Audio;
using TS3AudioBot.CommandSystem;
using TS3AudioBot.Plugins;
using TSLib.Full.Book;
using TSLib.Full;
using IniParser;
using IniParser.Model;
using System.Net.Sockets;
using MusicAPI;
using TSLib.Helper;
using TS3AudioBot.Config;
using System.Numerics;
using static System.Net.Mime.MediaTypeNames;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Timers;
using TSLib.Scheduler;
using TS3AudioBot.ResourceFactories;


namespace TS3AudioBot_Plugin_Netease_QQ
{
    public class Netease_QQ_plugin : IBotPlugin
    {
        private PlayManager playManager;
        private Ts3Client ts3Client;
        private InvokerData invokerData;
        private Player player;
        private Connection connection;

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

        // API
        private static MusicAPI.MusicAPI musicapi;
        // 播放
        // 播放列表, 结构List<"id": <id>,"music_type" :<音乐API选择(0为网易云, 1为QQ音乐)>>
        private List<Dictionary<string, string>> PlayList = new List<Dictionary<string, string>>();

        // 播放类型, 0:常规PlayList播放, 1:私人FM(仅限于网易云音乐)
        private int play_type = 0;
        // PlayList播放的播放模式, 0:顺序播放(到末尾自动暂停), 1:单曲循环, 2:顺序循环, 3:随机
        private int play_mode = 1;
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
        private bool isLyric = true;
        private string lyric_id_now;
        // 等待频道无人时间
        private readonly static int max_wait_alone = 30;
        private int waiting_time = 0;
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
            // 欢迎词
            await ts3Client.SendChannelMessage("TS3AudioBot-Plugin-Netease-QQ插件加载完毕");
        }
        //--------------------------歌词线程--------------------------
        private void StartLyric(bool enable)
        {// 启动或者关闭歌词线程
            if(enable)
            {
                if(Lyric_thread == null)
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
                        Console.WriteLine("1 id:"+string_id);
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
            if (PlayList.Count !=0 && play_index < PlayList.Count)
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
                    throw new Exception("(PlayMusic)中: "+e.Message);
                }
            }
        }
        public async Task PlayFMNow()
        {
            // 直接播放FM
            PlayList.Clear();
            play_index = 0;
            string fm_id;
            try
            {
                fm_id = await musicapi.GetFMSongId();
                await PlayListAdd(fm_id, 0);
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
                await ts3Client.SendChannelMessage($"加入{string_type}: id\"{song_id}\", {song_name}-{song_author}, 索引: {idx + 1}");
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
            }
            else
            {
                if(play_type == 0)
                {
                    // 常规歌单
                    if (play_mode == 1 || play_mode == 2)
                    {
                        // 列表顺序播放, 或者单曲循环
                        if(play_index + 1 >= PlayList.Count)
                        {
                            // 列表末尾
                            _ = MainCommands.CommandBotName(ts3Client, botname_connect);
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
                    else if(play_mode == 5)
                    {
                        // 顺序销毁模式
                        PlayList.Remove(PlayList[play_index]);
                        if(PlayList.Count == 0)
                        {
                            _ = MainCommands.CommandBotName(ts3Client, botname_connect);
                            await ts3Client.SendChannelMessage("歌单已经到底了");
                        }
                        else
                        {
                            await PlayListPlayNow();
                        }
                    }
                    else if(play_mode == 6)
                    {
                        // 随机销毁模式
                        PlayList.Remove(PlayList[play_index]);
                        if (PlayList.Count == 0)
                        {
                            _ = MainCommands.CommandBotName(ts3Client, botname_connect);
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
                {
                    // 私人FM
                    await PlayFMNow();
                }
            }
        }
        public async Task PlayListPre()
        {
            // 播放上一首
            if(play_type == 0)
            {
                if(PlayList.Count <= 1)
                {
                    await ts3Client.SendChannelMessage("无法上一首播放");
                }
                else
                {
                    if(play_index == 0)
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
            else if(play_type == 1)
            {
                await ts3Client.SendChannelMessage("FM模式不支持上一首播放");
            }
        }
        public async Task PlayListShow(int page=1)
        {
            // 展示歌单
            StringBuilder playlist_string_builder = new StringBuilder();
            playlist_string_builder.AppendLine("歌单如下:");
            playlist_string_builder.AppendLine(FormatLine("索引", "歌曲的ID", "歌曲名", "歌手", "来源", 8, 20, 30, 20, 10));
            for (int i = (page- 1)*10; i< PlayList.Count && i < page * 10 ; i++)
            {
                // 遍历列表
                string index_string, id_string, name_string, author_string, music_type_string;
                index_string = i == play_index? (i + 1).ToString()+"*" : (i + 1).ToString();
                PlayList[i].TryGetValue("id", out id_string);
                PlayList[i].TryGetValue("name", out name_string);
                PlayList[i].TryGetValue("author", out author_string);
                PlayList[i].TryGetValue("music_type", out music_type_string);
                music_type_string = music_type_string == "0" ? "网易云" : music_type_string == "1" ? "QQ音乐" : "未知";
                playlist_string_builder.AppendLine(FormatLine(index_string, id_string, name_string, author_string, music_type_string, 8, 20, 30, 20, 10));
            }
            playlist_string_builder .AppendLine($"第{page}页,共{PlayList.Count / 10 + 1}页|正在播放第{play_index+1}首歌,共{PlayList.Count}首歌");
            string playlist_string = playlist_string_builder.ToString();
            await ts3Client.SendChannelMessage(playlist_string);
        }
        static string FormatLine(string index, string id,string name,string author, string musictype, int indexWidth, int idWidth, int nameWidth, int authorWidth, int musictypeWidth)
        {
            // 根据中英文字符宽度调整字符串
            return $"{PadWithDots(index, indexWidth)}|" +
           $"{PadWithDots(id, idWidth)}|" +
           $"{PadWithDots(name, nameWidth)}|" +
           $"{PadWithDots(author, authorWidth)}|" +
           $"{PadWithDots(musictype, musictypeWidth)}";
        }
        static string PadWithDots(string input, int maxWidth)
        {
            if (string.IsNullOrEmpty(input))
                return new string('.', maxWidth);
            int currentWidth = 0;
            StringBuilder result = new StringBuilder();
            bool needsEllipsis = false;
            // 计算可用宽度（预留至少1个点号的位置）
            int availableWidth = maxWidth - 1;
            // 先填充内容（可能带截断）
            foreach (char c in input)
            {
                int charWidth = IsChinese(c) ? 2 : 1;

                if (currentWidth + charWidth > availableWidth)
                {
                    needsEllipsis = true;
                    break;
                }
                result.Append(c);
                currentWidth += charWidth;
            }
            // 添加点号填充剩余空间
            int remainingWidth = maxWidth - currentWidth;
            // 如果内容过长需要显示截断提示
            if (needsEllipsis)
            {
                // 确保至少能显示1个点号
                if (remainingWidth < 1)
                {
                    result.Remove(result.Length - 1, 1);
                    remainingWidth += IsChinese(result[result.Length - 1]) ? 2 : 1;
                }
                result.Append('.');
                remainingWidth -= 1;
            }
            // 填充剩余空白
            while (remainingWidth > 0)
            {
                result.Append('.');
                remainingWidth--;
            }
            return result.ToString();
        }
        // 判断字符是否为中文
        static bool IsChinese(char c)
        {
            if ((c >= 0xFF01 && c <= 0xFF60) || // 全角ASCII字符和全角空格
                (c >= 0xFFE0 && c <= 0xFFE6) || // 全角符号
                (c >= 0x3000 && c <= 0x303F) || // CJK标点符号
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
            if(music_type == 0)
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
            else if(music_type == 1)
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
                        string native_url = "";
                        if (music_type == 0)
                        {
                            native_url = $"https://music.163.com/#/song?id={Songid}";
                        }
                        else if (music_type == 1) 
                        {
                            native_url = $"https://y.qq.com/n/ryqq/songDetail/{Songid}";
                        }
                        var ar = new AudioResource(native_url, authorname, "media")
                            .Add("PlayUri", picurl);
                        await playManager.Play(invokerData, new MediaPlayResource(songurl, ar, await musicapi.HttpGetImage(picurl), false));
                        
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
                    newname = $"{botname_connect} {modename} {songname}-{authorname}";
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
                        await ts3Client.SendChannelMessage($"播放歌曲{songname} - {authorname}, 第{play_index + 1}首 共{PlayList.Count}首");
                    }
                    else
                    {
                        // 通知
                        await ts3Client.SendChannelMessage($"播放歌曲{songname} - {authorname}");
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
        //--------------------------指令段--------------------------
        [Command("test")]
        public async Task CommandTest(PlayManager playManager, Player player, Ts3Client ts3Client,ConfBot confbot)
        {
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
            string res = await musicapi.GetmidFromId("375869866");
            Console.WriteLine(res);
        }
        [Command("bgm play")]
        public async Task CommandMusicPlay(string argments, Ts3Client ts3Client)
        {
            await CommandWyyPlay(argments, ts3Client);
        }
        [Command("bgm seek")]
        public async Task CommandSeek(string argments)
        {
            long value = 0;
            if(long.TryParse(argments, out value))
            {
                if(!player.Paused && playManager.IsPlaying)
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
        [Command("bgm next")]
        public async Task CommandNext(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            await PlayListNext();
        }
        [Command("bgm pre")]
        public async Task CommandPre(Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            await PlayListPre();
        }
        [Command("bgm mode")]
        public async Task CommandMode(int argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if(1<=argments && argments<=6)
            {
                if (play_type == 0)
                {
                    play_mode = argments;
                    string notice = "";
                    switch (play_mode)
                    {
                        case 1: notice = "顺序播放模式";break;
                        case 2: notice = "单曲循环模式"; break;
                        case 3: notice = "顺序循环模式"; break;
                        case 4: notice = "随机播放模式"; break;
                        case 5: notice = "顺序销毁模式"; break;
                        case 6: notice = "随机销毁模式"; break;
                        default: break;
                    }
                    await ts3Client.SendChannelMessage(notice);
                }
                else if(play_type == 1)
                {
                    await ts3Client.SendChannelMessage("处于FM模式, 无法切换播放模式");
                }
            }
            else
            {
                await ts3Client.SendChannelMessage("输入参数错误");
            }
        }
        [Command("bgm ls")]
        public async Task CommandLs()
        {
            await PlayListShow(play_index / 10 + 1);
        }
        [Command("bgm ls p")]
        public async Task CommandLsPage(int page, InvokerData invokerData)
        {
            // 展示第page页
            await PlayListShow(page);
        }
        [Command("bgm go")]
        public async Task CommandGo(int argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 音乐跳转
            if (0<argments && argments <=PlayList.Count)
            {
                play_index = argments - 1;
                await PlayListPlayNow();
            }
            else
            {
                await ts3Client.SendChannelMessage($"超出索引范围, 范围[1,{PlayList.Count}]");
            }
        }
        [Command("bgm mv")]
        public async Task CommandMv(int idx, int target, Ts3Client ts3Client)
        {// idx为要移动的歌曲, target为目标, 范围是[1,PlayList.Count], 若target为
            if(idx < 1 || idx > PlayList.Count)
            {
                await ts3Client.SendChannelMessage($"idx: {idx}超出索引, 范围[1,{PlayList.Count}]");
                return;
            }
            if (target < 1 || target > PlayList.Count)
            {
                await ts3Client.SendChannelMessage($"target: {target}超出索引, 范围[1,{PlayList.Count}]");
                return;
            }
            if(idx == target)
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
            await PlayListShow((target- 1) / 10 + 1);
        }
        [Command("bgm rm")]
        public async Task CommandRm(int argments, Ts3Client ts3Client, Player player)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 歌单删除
            if (1 <= argments && argments <= PlayList.Count)
            {
                string name = "";
                PlayList[argments-1].TryGetValue("name", out name);
                await ts3Client.SendChannelMessage($"删除歌曲{name}");
                PlayList.Remove(PlayList[argments-1]);

                if (play_index == argments-1)
                {
                    // 删除正在播放的歌曲
                    if (play_index < PlayList.Count)
                    {
                        if(!player.Paused || !playManager.IsPlaying)
                        {// 正在播放
                            await PlayListPlayNow();
                        }
                    }
                    else
                    {
                        play_index = 0;
                        if (!player.Paused || !playManager.IsPlaying)
                        {// 正在播放
                            if(PlayList.Count>0)
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
                else if (play_index< argments - 1)
                {
                    play_index--;
                }
            }
            else
            {
                await ts3Client.SendChannelMessage($"超出索引范围, 范围[1,{PlayList.Count}]");
            }
        }
        [Command("bgm clear")]
        public async Task CommandClearList(Ts3Client ts3Client, Player player)
        {
            PlayList.Clear();
            play_index = 0;
            lyric_before = "";
            lyric_id_now = "";
            if (!player.Paused || !playManager.IsPlaying)
            {
                MainCommands.CommandPause(player);
                MainCommands.CommandStop(playManager);
            }
            await ts3Client.SendChannelMessage($"歌单清空");
            if(botname_connect_before != botname_connect)
            {
                await ts3Client.ChangeName(botname_connect);
                botname_connect_before = botname_connect;
            }
            await ts3Client.ChangeDescription("");
            await ts3Client.DeleteAvatar();
        }
        //--------------------------歌词指令段--------------------------
        [Command("bgm lyric")]
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
                await CommandGo(play_index + 2, ts3Client);
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
                if (long.TryParse(argments, out id_gd) != true)
                {
                    // 输入为歌名
                    string name_gd = argments;
                    id_gd = await musicapi.SearchPlayList(name_gd, 0);
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
            await PlayFMNow();
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
        public async Task CommandQQLoad( Ts3Client ts3Client)
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
            if(PlayList.Count == 0)
            {
                await CommandQQAdd(argments, ts3Client);
            }
            else
            {
                await CommandQQInsert(argments, ts3Client);
                await CommandGo(play_index + 2, ts3Client);
            }
        }
        [Command("qq insert")]
        public async Task CommandQQInsert(string argments, Ts3Client ts3Client)
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
                string id = "";
                string songname = "";
                // 判断是否为mid
                Regex pureNumRgx = new Regex(@"^\d+$");      // 纯数字正则
                Regex alnumRgx = new Regex("^[a-zA-Z0-9]+$");// 字母数字组合正则

                if (pureNumRgx.IsMatch(argments))            // 情况1：纯数字
                {
                    // 输入为纯数字ID
                    try
                    {
                        id = await musicapi.GetmidFromId(argments);
                        await ts3Client.SendChannelMessage($"输入数字ID\"{argments}\", 得到mid\"{id}\"");
                        await PlayListAdd(id, 1, play_index + 1);
                    }
                    catch (Exception e)
                    {
                        await ts3Client.SendChannelMessage("在songid转songmid的过程中出现问题: " + e.Message);
                    }
                }
                else if (alnumRgx.IsMatch(argments))         // 情况2：字母数字组合
                {
                    // 输入为mid
                    await PlayListAdd(argments, 1, play_index + 1);
                }
                else // 情况3：其他字符
                {
                    // 输入为歌名
                    songname = argments;
                    Dictionary<string, string> res = await musicapi.SearchSong(songname, 1);
                    string songname_get;
                    string author_get;
                    songname_get = res["name"];
                    author_get = res["author"];
                    id = res["id"];
                    await ts3Client.SendChannelMessage($"搜索歌\"{songname}\"得到\"{songname_get}-{author_get}\", id\"{id}\"");
                    await PlayListAdd(id, 1, play_index + 1);
                }
            }
        }
        [Command("qq add")]
        public async Task CommandQQAdd(string argments, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            string id = "";
            string songname = "";
            // 判断是否为mid
            Regex pureNumRgx = new Regex(@"^\d+$");      // 纯数字正则
            Regex alnumRgx = new Regex("^[a-zA-Z0-9]+$");// 字母数字组合正则

            if (pureNumRgx.IsMatch(argments))            // 情况1：纯数字
            {
                // 输入为纯数字ID
                try
                {
                    id = await musicapi.GetmidFromId(argments);
                    await ts3Client.SendChannelMessage($"输入数字ID\"{argments}\", 得到mid\"{id}\"");
                    await PlayListAdd(id, 1);
                }
                catch (Exception e)
                {
                    await ts3Client.SendChannelMessage("在songid转songmid的过程中出现问题: " +e.Message);
                }
            }
            else if (alnumRgx.IsMatch(argments))         // 情况2：字母数字组合
            {
                // 输入为mid
                await PlayListAdd(argments, 1);
            }
            else // 情况3：其他字符
            {
                // 输入为歌名
                songname = argments;
                Dictionary<string, string> res = await musicapi.SearchSong(songname, 1);
                string songname_get;
                string author_get;
                songname_get = res["name"];
                author_get = res["author"];
                id = res["id"];
                await ts3Client.SendChannelMessage($"搜索歌\"{songname}\"得到\"{songname_get}-{author_get}\", id\"{id}\"");
                await PlayListAdd(id, 1);
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
            if (long.TryParse(argments, out id_gd) != true)
            {
                // 输入为歌名
                string name_gd = argments;
                id_gd = await musicapi.SearchPlayList(name_gd, 1);
            }
            try
            {
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
        //--------------------------事件--------------------------
        private async Task OnSongStop(object sender, EventArgs e)
        {
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
            this.invokerData = value.Invoker;
            return Task.CompletedTask;
        }
        private async Task OnAlone(object sender, EventArgs e)
        {
            var args = e as AloneChanged;
            if (args != null)
            {
                if(args.Alone)
                {// 频道无人
                    if(!player.Paused || !playManager.IsPlaying)
                    {
                        waiting_time = 1;
                        while(waiting_time  <= max_wait_alone && waiting_time !=0)
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
