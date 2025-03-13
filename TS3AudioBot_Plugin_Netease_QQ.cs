using System;
using System.Collections.Generic;
using System.IO;
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
using System.Xml.Linq;
using TSLib.Helper;
using System.Linq;

namespace TS3AudioBot_Plugin_Netease_QQ
{
    public class Netease_QQ_plugin : IBotPlugin
    {
        private PlayManager playManager;
        private Ts3Client ts3Client;
        private InvokerData invokerData;
        private Player player;
        private Connection connection;

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
        // PlayList播放的播放模式, 0:顺序播放(到末尾自动暂停), 1:单曲循环, 2:顺序循环, 3:随机循环
        private int play_mode = 0;
        // PlayList的播放index
        private int play_index = 0;
        // 是否正在获取歌单标志位
        private bool isGetingGd = false;
        //--------------------------获取audio bot数据--------------------------
        public Netease_QQ_plugin(PlayManager playManager,Ts3Client ts3Client, Player player, Connection connection)
        {
            this.playManager = playManager;
            this.ts3Client = ts3Client;
            this.player = player;
            this.connection = connection;
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
                iniPath = "plugins" + config_file_name;
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
            
        }
        //--------------------------歌单操作--------------------------
        public async Task PlayListPlayNow()
        {
            // 歌单 播放当前index 的歌
            if(PlayList.Count !=0 && play_index < PlayList.Count)
            {
                string string_id = "";
                string string_music_type = "";
                int music_type = 0;
                PlayList[play_index].TryGetValue("id", out string_id);
                PlayList[play_index].TryGetValue("music_type", out string_music_type);
                int.TryParse(string_music_type, out music_type);
                await PlayMusic(string_id, (int) music_type);
            }
        }
        public async Task PlayFMNow()
        {
            // 直接播放FM
            PlayList.Clear();
            play_index = 0;
            string fm_id = await musicapi.GetFMSongId();
            await PlayListAdd(fm_id, 0);
        }

        public async Task<string> PlayListAdd(string song_id, int music_type)
        {
            // 歌单添加歌 同时返回歌名
            string song_name = "", song_author = "";
            Dictionary<string, string> detail = await musicapi.GetSongDetail(song_id, music_type);
            detail.TryGetValue("name", out song_name);
            detail.TryGetValue("author", out song_author);
            // 添加歌单
            PlayList.Add(new Dictionary<string, string> {
             {"id", song_id },
             { "music_type", music_type.ToString() },
             { "name", song_name },
             {"author", song_author }
            });
            if (PlayList.Count == 1)
            {
                // 直接播放
                play_index = 0;
                await PlayListPlayNow();
            }
            return $"{song_name}-{song_author}";
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
            string songurl = await musicapi.GetSongUrl(Songid, music_type);
            // await ts3Client.SendChannelMessage("获得URL:");
            // await ts3Client.SendChannelMessage(songurl);
            Dictionary<string, string> detail = await musicapi.GetSongDetail(Songid, music_type);
            if (songurl == null || songurl == "")
            {
                _ = ts3Client.SendChannelMessage("url获取错误 ");
            }
            else
            {
                // 加入音乐url
                await MainCommands.CommandPlay(playManager, invokerData, songurl);
                // 修改机器人描述
                string error = "";
                if (detail.TryGetValue("error", out error))
                {
                    _ = ts3Client.SendChannelMessage(error);
                }
                else
                {
                    string songname = "名称获取失败", authorname = "", picurl = "";
                    detail.TryGetValue("name", out songname);
                    detail.TryGetValue("author", out authorname);
                    detail.TryGetValue("picurl", out picurl);
                    // 修改机器人描述和头像
                    string modename = "";
                    if(play_type==0){
                        if (play_mode == 0){
                            modename = "[顺序播放]";
                        }else if (play_mode == 1){
                            modename = "[单曲循环]";
                        }else if(play_mode == 2){
                            modename = "[顺序循环]";
                        }else if(play_mode == 3){
                            modename = "[随机循环]";
                        }
                    }else{
                        modename = "[FM模式]";
                    }
                    _ = MainCommands.CommandBotDescriptionSet(ts3Client, modename + songname + "-" + authorname);
                    _ = MainCommands.CommandBotAvatarSet(ts3Client, picurl);
                    // 通知
                    _ = ts3Client.SendChannelMessage($"播放歌曲{songname} - {authorname}");
                }
            }
        }
        //--------------------------指令段--------------------------
        [Command("test")]
        public async Task CommandTest(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            //Dictionary<string,string> detail = new Dictionary<string,string>();
            await ts3Client.SendChannelMessage(musicapi.cookies[1]);
        }

        [Command("bgm play")]
        public async Task CommandMusicPlay(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
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

        [Command("bgm next")]
        public async Task CommandNext(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            this.invokerData = invokerData;
            await PlayListNext();
        }

        [Command("bgm pre")]
        public async Task CommandPre(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            this.invokerData = invokerData;
            await PlayListPre();
        }

        [Command("bgm mode")]
        public async Task CommandMode(int argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
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
                        case 3: notice = "随机循环模式"; break;
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
                await ts3Client.SendChannelMessage("输出参数错误");
            }
        }

        [Command("bgm ls")]
        public async Task CommandLs(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            // 展示第1页
            this.invokerData = invokerData;
            await PlayListShow(1);
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
            // 歌单删除
            if (0 <= argments && argments < PlayList.Count)
            {
                if(play_index == argments)
                {
                    // 删除正在播放的歌曲
                    play_index = 0;
                    MainCommands.CommandPause(player);
                }
                string name = "";
                PlayList[argments].TryGetValue("name", out name);
                await ts3Client.SendChannelMessage($"删除歌曲{name}");
                PlayList.Remove(PlayList[argments]);
            }
            else
            {
                await ts3Client.SendChannelMessage("超出索引范围");
            }
        }

        [Command("bgm clear")]
        public async Task CommandClearList(int argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
            PlayList.Clear();
            play_index = 0;
            await ts3Client.SendChannelMessage($"歌单清空");
        }

        //--------------------------网易云指令段--------------------------
        [Command("wyy login")]
        public async Task CommandWyyLogin(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            // 网易云音乐登录
            this.invokerData = invokerData;
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
                    musicapi.SetCookies(cookies, 0);
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
        }
        [Command("wyy play")]
        public async Task CommandWyyPlay(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
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
                await ts3Client.SendChannelMessage($"输入id{argments}");
                // 输入为id
                string res = await PlayListAdd(id.ToString(), 0);
                await ts3Client.SendChannelMessage($"{res}已加入歌单");
            }
            else
            {
                // 输入为歌名
                songname = argments;
                long.TryParse(await musicapi.SearchSong(songname, 0), out id);
                await ts3Client.SendChannelMessage($"搜索歌{songname}-得到ID:{id}");
                string res = await PlayListAdd(id.ToString(), 0);
                await ts3Client.SendChannelMessage($"{res}已加入歌单");
            }
        }
        
        [Command("wyy gd")]
        public async Task CommandWyyGd(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.invokerData = invokerData;
            if(isGetingGd)
            {
                await ts3Client.SendChannelMessage("歌单获取中，请稍后重试");
            }
            else
            {
                await ts3Client.SendChannelMessage("开始获取歌单");
                isGetingGd = true;
                long id_gd = 0;
                if (long.TryParse(argments, out id_gd)!=true)
                {
                    // 输入为歌名
                    string name_gd = argments;
                    id_gd = await musicapi.SearchPlayList(name_gd, 0);
                }
                List<Dictionary<string, string>> detail_playlist = await musicapi.GetPlayListDetail(id_gd.ToString(), 0);
                // 清空当前list
                PlayList.Clear();
                play_index = 0;
                
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
                await ts3Client.SendChannelMessage($"导入歌单完成, 共{detail_playlist.Count}首歌");
                await PlayListPlayNow();
                isGetingGd = false;
            }
        }

        [Command("wyy fm")]
        public async Task CommandWyyFM(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
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
        public async Task CommandQQLogin(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            this.invokerData = invokerData;
            string res = await musicapi.SetQQLoginCookies(argments);
            if (res == "true")
            {
                // 登录成功，保存cookie
                string cookies = "\"" + argments + "\"";
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
        [Command("qq load")]
        public async Task CommandQQLoad(PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            // 读取本地中的cookies，然后将他保存到qqmusic api
            this.invokerData = invokerData;
            plugin_config = plugin_config_parser.ReadFile(iniPath);
            qqmusic_cookies = plugin_config["qq"]["cookies"];
            qqmusic_cookies = qqmusic_cookies.Trim(new char[] { '"' });
            qqmusic_cookies = string.IsNullOrEmpty(qqmusic_cookies) ? "" : qqmusic_cookies;
            await CommandQQLogin(qqmusic_cookies, playManager, invokerData, ts3Client);
        }

        [Command("qq play")]
        public async Task CommandQQPlay(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client, Player player)
        {
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
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.invokerData = invokerData;

            string id = "";
            string songname = "";
            
            if (await musicapi.GetSongUrl(argments,1) != "")
            {

                // 输入为mid
                await ts3Client.SendChannelMessage($"输入mid{argments}");
                string res = await PlayListAdd(argments, 1);
                await ts3Client.SendChannelMessage($"{res}已加入歌单");
            }
            else
            {
                // 输入为歌名
                songname = argments;
                id = await musicapi.SearchSong(songname, 1);
                await ts3Client.SendChannelMessage($"搜索歌名{songname}-得到ID:{id}");
                
                string res = await PlayListAdd(id, 1);
                await ts3Client.SendChannelMessage($"{res}已加入歌单");
            }
            
        }

        [Command("qq gd")]
        public async Task CommandQQGd(string argments, PlayManager playManager, InvokerData invokerData, Ts3Client ts3Client)
        {
            if (play_type != 0)
            {
                play_type = 0;
                await ts3Client.SendChannelMessage("切换至普通模式");
            }
            this.invokerData = invokerData;
            if (isGetingGd)
            {
                await ts3Client.SendChannelMessage("歌单获取中，请稍后重试");
            }
            else
            {
                await ts3Client.SendChannelMessage("开始获取歌单");
                isGetingGd = true;
                long id_gd = 0;
                if (long.TryParse(argments, out id_gd) != true)
                {
                    // 输入为歌名
                    string name_gd = argments;
                    id_gd = await musicapi.SearchPlayList(name_gd, 1);
                }
                List<Dictionary<string, string>> detail_playlist = await musicapi.GetPlayListDetail(id_gd.ToString(), 1);
                // 清空当前list
                PlayList.Clear();
                play_index = 0;

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
                await ts3Client.SendChannelMessage($"导入歌单完成, 共{detail_playlist.Count}首歌");
                await PlayListPlayNow();
                isGetingGd = false;
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

        public void Dispose()
        {
            // Don't forget to unregister everything you have subscribed to,
            // otherwise your plugin will remain in a zombie state
            plugin_config_parser.WriteFile(iniPath, plugin_config);
            player.OnSongEnd -= OnSongEnd;
        }

    }
}
