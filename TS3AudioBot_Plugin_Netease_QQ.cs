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
        private int play_mode = 0;
        // PlayList的播放index
        private int play_index = 0;
        // 是否阻塞
        private bool isObstruct = false;
        // 当前播放的歌词
        private List<Dictionary<TimeSpan, string>> Lyric = new List<Dictionary<TimeSpan, string>>();
        // 上一个歌词
        private string lyric_before;
        // 是否展示歌词标志位
        private bool isLyric = false;
        private bool isLyricThread = false;
        private string lyric_id_now;
        // 等待频道无人时间
        private static int max_wait_alone = 30;
        private int waiting_time = 0;
        //--------------------------获取audio bot数据--------------------------
        public Netease_QQ_plugin(PlayManager playManager,Ts3Client ts3Client, Player player, Connection connection,ConfBot confBot)
        {
            this.playManager = playManager;
            this.ts3Client = ts3Client;
            this.player = player;
            this.connection = connection;
            this.botname_connect = confBot.Connect.Name;
        }

        //--------------------------初始化--------------------------
        public void Initialize()
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
            // 读取配置
            plugin_config_parser = new FileIniDataParser();
            plugin_config = new IniData();
            plugin_config = plugin_config_parser.ReadFile(iniPath);
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
            player.OnSongEnd += OnSongEnd;
            ts3Client.OnAloneChanged += OnAlone;
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
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    // 歌词线程
                    if (isLyricThread == true)
                    {
                        isLyricThread = false;
                        await Task.Delay(250);
                    }
                    isLyricThread = true;
                    while (isLyricThread)
                    {
                        await Task.Delay(200);
                        if (this.PlayList.Count == 0) { continue; }
                        if (player.Paused) { continue; }
                        if (!isLyric) { continue; }
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
                            await ts3Client.ChangeDescription(lyric_now);
                        }
                        lyric_before = lyric_now;
                    }
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
                await ts3Client.SendChannelMessage(e.Message);
            }
            catch (ArgumentException e)
            {
                await ts3Client.SendChannelMessage(e.Message);
            }
        }
        public async Task PlayListAdd(string song_id, int music_type)
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
                PlayList.Add(new Dictionary<string, string> {
                        {"id", song_id },
                        { "music_type", music_type.ToString() },
                        { "name", song_name },
                        {"author", song_author }
                 });
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage(e.Message);
            }
            try
            {
                if (PlayList.Count == 1)
                {
                    // 直接播放
                    play_index = 0;
                    await ts3Client.SendChannelMessage($"{song_name}-{song_author}已加入歌单");
                    await PlayListPlayNow();
                }
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage(e.Message);
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
                    Lyric = await musicapi.GetSongLyric(string_id, music_type);
                }
                catch (Exception e)
                {
                    await ts3Client.SendChannelMessage(e.Message);
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
                    if (play_mode == 0 || play_mode == 1)
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
                    else if (play_mode == 2)
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
                    else if (play_mode == 3)
                    {
                        // 列表随机循环播放
                        Random random = new Random();
                        play_index = random.Next(0, PlayList.Count);
                        await PlayListPlayNow();
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
                index_string = i == play_index? i.ToString()+"*" : i.ToString();
                PlayList[i].TryGetValue("id", out id_string);
                PlayList[i].TryGetValue("name", out name_string);
                PlayList[i].TryGetValue("author", out author_string);
                PlayList[i].TryGetValue("music_type", out music_type_string);
                music_type_string = music_type_string == "0" ? "网易云" : music_type_string == "1" ? "QQ音乐" : "未知";
                playlist_string_builder.AppendLine(FormatLine(index_string, id_string, name_string, author_string, music_type_string, 8, 20, 30, 20, 10));
            }
            playlist_string_builder .AppendLine($"第{page}页,共{PlayList.Count / 10 + 1}页|正在播放第{play_index}首歌,共{PlayList.Count}首歌");
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
                await ts3Client.SendChannelMessage(e.Message);
                return;
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage(e.Message);
            }
            string songurl = "";
            try
            {// 获取url
                songurl = await musicapi.GetSongUrl(Songid, music_type);
            }
            catch (Exception e)
            {
                await ts3Client.SendChannelMessage(e.Message);
            }
            if (songurl == null || songurl == "")
            {
                await ts3Client.SendChannelMessage("url获取错误 ");
            }
            else
            {
                // 加入音乐url
                try
                {
                    await MainCommands.CommandPlay(playManager, invokerData, songurl);
                }
                catch (Exception)
                {
                    await ts3Client.SendChannelMessage($"URL无法播放-{songurl}");
                    return;
                }
                // 修改机器人描述
                if (detail != null)
                {
                    string songname = "名称获取失败", authorname = "", picurl = "";
                    detail.TryGetValue("name", out songname);
                    detail.TryGetValue("author", out authorname);
                    detail.TryGetValue("picurl", out picurl);
                    // 修改机器人描述和头像
                    string modename = "";
                    if(play_type==0){
                        if (play_mode == 0){
                            modename = "[顺序]";
                        }else if (play_mode == 1){
                            modename = "[单曲循环]";
                        }else if(play_mode == 2){
                            modename = "[顺序循环]";
                        }else if(play_mode == 3){
                            modename = "[随机]";
                        }
                    }
                    else{
                        modename = "[FM]";
                    }
                    string newname = $"{botname_connect} {modename} {songname}-{authorname}";
                    if (newname.Length >= 21)
                    {
                        newname = newname.Substring(0, 21) +"...";
                    }
                    if(newname != botname_connect_before)
                    {// 相同则不换
                        await MainCommands.CommandBotName(ts3Client, newname);
                        botname_connect_before = newname;
                    }
                    if (play_type == 0)
                    {
                        // 通知
                        await ts3Client.SendChannelMessage($"播放歌曲{songname} - {authorname}, 第{play_index}首 共{PlayList.Count}首");
                    }
                    else
                    {
                        // 通知
                        await ts3Client.SendChannelMessage($"播放歌曲{songname} - {authorname}");
                    }
                    try
                    {
                        await MainCommands.CommandBotAvatarSet(ts3Client, picurl);
                    }
                    catch (Exception)
                    {
                        await ts3Client.SendChannelMessage($"机器人头像更换失败,url:{picurl}");
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
            await ts3Client.SendChannelMessage($"{player.Position}");
            // List<Dictionary<TimeSpan, string>> lyric = await musicapi.GetSongLyric("28285910", 0);
            //await ts3Client.SendChannelMessage($"{lyric[0].First().Key} = {lyric[0][lyric[0].First().Key]}");
            //await ts3Client.SendChannelMessage($"{lyric[5].First().Key} = {lyric[5][lyric[5].First().Key]}");
            // List<Dictionary<TimeSpan, string>> lyric = await musicapi.GetSongLyric("004MdHVz0vDB5C", 1);
            // await ts3Client.SendChannelMessage($"{Lyric.Count}");
            // await ts3Client.SendChannelMessage($"{lyric[0].First().Key} = {lyric[0][lyric[0].First().Key]}");
            // await ts3Client.SendChannelMessage($"{lyric[5].First().Key} = {lyric[5][lyric[5].First().Key]}");
            // isLyric = false;
            // await ts3Client.SendChannelMessage(flag_i.ToString());
        }

        [Command("bgm play")]
        public async Task CommandMusicPlay(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 单独音乐播放
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.PlayList.Clear();
            this.play_index = 0;
            this.invokerData = invokerData;
            await CommandWyyAdd(argments, playManager, invokerData, ts3Client);
        }

        /*
        [Command("bgm seek")]
        public async Task CommandSeek(string argments)
        {
            long value = 0;
            if(long.TryParse(argments, out value))
            {
                if(!player.Paused)
                {
                    long p_now, p_length;
                    p_now = (long)((TimeSpan)player.Position).TotalSeconds;
                    p_length = (long)((TimeSpan)player.Length).TotalSeconds;
                    if (value < p_length && value >= 0)
                    {
                        await player.Seek(TimeSpan.FromSeconds(value));
                    }
                }
            }
        }
        */
        [Command("bgm next")]
        public async Task CommandNext(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            this.invokerData = invokerData;
            await PlayListNext();
        }

        [Command("bgm pre")]
        public async Task CommandPre(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            this.invokerData = invokerData;
            await PlayListPre();
        }

        [Command("bgm mode")]
        public async Task CommandMode(int argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            this.invokerData = invokerData;
            if(0<=argments && argments<=3)
            {
                if (play_type == 0)
                {
                    play_mode = argments;
                    string notice = "";
                    switch (play_mode)
                    {
                        case 0: notice = "顺序播放模式";break;
                        case 1: notice = "单曲循环模式"; break;
                        case 2: notice = "顺序循环模式"; break;
                        case 3: notice = "随机播放模式"; break;
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
        public async Task CommandLs(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            this.invokerData = invokerData;
            await PlayListShow(play_index / 10 + 1);
        }
        [Command("bgm ls p")]
        public async Task CommandLsPage(int page, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            // 展示第page页
            this.invokerData = invokerData;
            await PlayListShow(page);
        }
        [Command("bgm go")]
        public async Task CommandGo(int argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 音乐跳转
            if (0<=argments && argments < PlayList.Count)
            {
                play_index = argments;
                await PlayListPlayNow();
            }
            else
            {
                await ts3Client.SendChannelMessage("超出索引范围");
            }
        }
        [Command("bgm rm")]
        public async Task CommandRm(int argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 歌单删除
            if (0 <= argments && argments < PlayList.Count)
            {
                string name = "";
                PlayList[argments].TryGetValue("name", out name);
                await ts3Client.SendChannelMessage($"删除歌曲{name}");
                PlayList.Remove(PlayList[argments]);

                if (play_index == argments)
                {
                    // 删除正在播放的歌曲
                    if (play_index < PlayList.Count)
                    {
                        if(!player.Paused)
                        {// 正在播放
                            await PlayListPlayNow();
                        }
                    }
                    else
                    {
                        play_index = 0;
                        if (!player.Paused)
                        {// 正在播放
                            if(PlayList.Count>0)
                            {// 列表有歌曲
                                await PlayListPlayNow();
                            }
                            else
                            {
                                MainCommands.CommandPause(player);
                            }
                        }
                    }
                }
            }
            else
            {
                await ts3Client.SendChannelMessage("超出索引范围");
            }
        }
        [Command("bgm clear")]
        public async Task CommandClearList(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            PlayList.Clear();
            play_index = 0;
            if(!player.Paused)
            {
                MainCommands.CommandPause(player);
            }
            await ts3Client.SendChannelMessage($"歌单清空");
        }
        //--------------------------歌词指令段--------------------------
        [Command("bgm lyric")]
        public async Task CommandLyric(PlayManager playManager, Ts3Client ts3Client, Player player)
        {
            // 通过用户的指令来创建歌词线程
            if (isLyric)
            {
                isLyric = false;
                await ts3Client.SendChannelMessage("歌词已关闭");
            }
            else
            {
                isLyric = true;
                await ts3Client.SendChannelMessage("歌词已开启");
            }
        }
        //--------------------------网易云指令段--------------------------
        [Command("wyy login")]
        public async Task CommandWyyLogin(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            this.invokerData = invokerData;
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
        [Command("wyy play")]
        public async Task CommandWyyPlay(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 单独音乐播放
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.PlayList.Clear();
            this.play_index = 0;
            this.invokerData = invokerData;
            await CommandWyyAdd(argments, playManager, invokerData, ts3Client);
        }
        [Command("wyy add")]
        public async Task CommandWyyAdd(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.invokerData = invokerData;
            long id = 0;
            string songname = "";
            if (long.TryParse(argments, out id))
            {
                await ts3Client.SendChannelMessage($"输入id\"{argments}\"");
                // 输入为id
                await PlayListAdd(id.ToString(), 0);
            }
            else
            {
                // 输入为歌名
                songname = argments;
                long.TryParse(await musicapi.SearchSong(songname, 0), out id);
                await ts3Client.SendChannelMessage($"搜索歌{songname}-得到id\"{id}\"");
                await PlayListAdd(id.ToString(), 0);
            }
        }
        [Command("wyy gd")]
        public async Task CommandWyyGd(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 添加歌单
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.invokerData = invokerData;
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
                await PlayListPlayNow();
            }
            catch (Exception)
            {
                isObstruct = false;
                throw;
            }
        }
        [Command("wyy fm")]
        public async Task CommandWyyFM(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
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
        public async Task CommandQQLogin(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
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
                await ts3Client.SendChannelMessage(e.Message);
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
        public async Task CommandQQLoad(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 读取本地中的cookies，然后将他保存到qqmusic api
            this.invokerData = invokerData;
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
        public async Task CommandQQPlay(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            // 单独音乐播放
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.PlayList.Clear();
            this.play_index = 0;
            this.invokerData = invokerData;
            await CommandQQAdd(argments, playManager, invokerData, ts3Client);
        }
        [Command("qq add")]
        public async Task CommandQQAdd(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.invokerData = invokerData;
            string id = "";
            string songname = "";
            // 判断是否为mid
            Regex rgx = new Regex("^[a-zA-Z0-9]+$");
            
            if (rgx.IsMatch(argments))
            {
                // 输入为mid
                await ts3Client.SendChannelMessage($"输入id\"{argments}\"");
                await PlayListAdd(argments, 1);
            }
            else
            {
                // 输入为歌名
                songname = argments;
                id = await musicapi.SearchSong(songname, 1);
                await ts3Client.SendChannelMessage($"搜索歌名{songname}-得到id\"{id}\"");
                await PlayListAdd(id, 1);
            }
            
        }
        [Command("qq gd")]
        public async Task CommandQQGd(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (isObstruct) { await ts3Client.SendChannelMessage("正在进行处理，请稍后"); return; }
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.invokerData = invokerData;
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
                await PlayListPlayNow();
            }
            catch (Exception)
            {
                isObstruct = false;
                throw;
            }
        }
        //--------------------------事件--------------------------
        private async Task OnSongEnd(object sender, EventArgs e)
        {
            if (play_type == 0 && play_mode == 1)
            {
                // 单曲循环
                await PlayListPlayNow();
            }
            else
            {
                // 下一首
                await PlayListNext();
            }
        }
        private async Task OnAlone(object sender, EventArgs e)
        {
            var args = e as AloneChanged;
            if (args != null)
            {
                if(args.Alone)
                {// 频道无人
                    if(!player.Paused)
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
        public void Dispose()
        {
            // Don't forget to unregister everything you have subscribed to,
            // otherwise your plugin will remain in a zombie state
            plugin_config_parser.WriteFile(iniPath, plugin_config);
            player.OnSongEnd -= OnSongEnd;
            ts3Client.OnAloneChanged -= OnAlone;
            if (isLyric == true) 
            {
                isLyric = false;
            }
        }

    }
}
