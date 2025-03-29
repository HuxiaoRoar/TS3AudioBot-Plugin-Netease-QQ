using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using TSLib.Helper;
using Newtonsoft.Json;
using System.Diagnostics;
using TS3AudioBot.Playlists;
using System.Data;
using System.Text.RegularExpressions;
using TS3AudioBot;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Collections.Specialized;
using System.Linq;
using System.Globalization;
using System.Reflection.Metadata;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace MusicAPI
{
    public class MusicAPI
    {
        // 0位为网易云音乐 1位为QQ音乐
        public List<string> cookies = new List<string> { "", "" };
        public List<string> addresss = new List<string> { "", "" };
        public string netease_login_key;
        public string ptqrtoken;
        public string qrsig;

        // 报错信息文本
        private static string error_http_error = "http请求发生错误";
        private static string error_json_error = "Json解析错误，是否部署了正确api，是否登录";
        // private static string error_error_no_detail = "获取歌曲信息失败，请检查";
        // private static string error_no_url = "获取歌曲URL失败，检查是否登录";

        // 重试参数
        private static int maxtry = 2;
        private static int trydelay = 1000;

        //--------------------------参数设置--------------------------
        public void SetCookies(string cookies, int type_music)
        {
            // 设置cookies，type_music为选择是哪一个
            this.cookies[type_music] = cookies;
        }
        public void SetAddress(string address, int type_music)
        {
            this.addresss[type_music] = address;
        }
        //--------------------------登录--------------------------
        public async Task<Stream> GetNeteaseLoginImage()
        {
            // 获取登录key
            string login_key_url = $"{addresss[0]}/login/qr/key?timestamp={GetTimeStamp()}";
            string Json_get = await HttpGetAsync(login_key_url);
            if (Json_get == null)
            {
                return null;
            }
            dynamic data = JsonConvert.DeserializeObject<dynamic>(Json_get);
            netease_login_key = data.data.unikey;
            // 获取登录二维码
            string image_url = $"{addresss[0]}/login/qr/create?key={netease_login_key}&qrimg=true&timestamp={GetTimeStamp()}";
            Json_get = await HttpGetAsync(image_url);
            if (Json_get == null)
            {
                return null;
            }
            data = JsonConvert.DeserializeObject<dynamic>(Json_get);
            string img_string = data.data.qrimg;
            // string转stream
            string[] img = img_string.Split(",");
            byte[] bytes = Convert.FromBase64String(img[1]);
            Stream stream = new MemoryStream(bytes);

           return stream;
        }
        public async Task<Dictionary<string,string>> CheckNeteaseLoginStatus()
        {
            // 检查登录情况
            string check_url = $"{addresss[0]}/login/qr/check?key={netease_login_key}&timestamp={GetTimeStamp()}";
            string Json_get = await HttpGetAsync(check_url);
            Dictionary<string,string> re = new Dictionary<string,string>();
            if (Json_get == null)
            {
                return null;
            }
            dynamic data = JsonConvert.DeserializeObject<dynamic>(Json_get);
            int code = data.code;
            string message = data.message;
            string cookie = data.cookie;
            re.Add("code",code.ToString());
            re.Add("message", message);
            re.Add("cookie", cookie);
            return re;
        }
        
        public async Task<Stream> GetQQLoginImage()
        {
            // 获取登录二维码
            string login_key_url = $"{addresss[1]}/user/getLoginQr/qq";
            string Json_get;
            try
            {
                Json_get = await HttpGetAsync(login_key_url);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"访问QQ音乐二维码登录api失败, 检查是否部署支持扫码登录的API-msg:{e.Message}-{login_key_url}");
            }
            if (Json_get == null)
            {
                return null;
            }
            dynamic data = JsonConvert.DeserializeObject<dynamic>(Json_get);
            string image;
            image = data.img;
            ptqrtoken = data.ptqrtoken;
            qrsig = data.qrsig;
            
            // string转stream
            string[] img = image.Split(",");
            byte[] bytes = Convert.FromBase64String(img[1]);
            Stream stream = new MemoryStream(bytes);

            return stream;
        }
        public async Task<Dictionary<string,string>> CheckQQLoginStatus()
        {
            // 检查登录情况
            string check_url = $"{addresss[1]}/user/checkLoginQr/qq";
            string content = String.Format("{{\"ptqrtoken\" : \"{0}\",\"qrsig\": \"{1}\"}}", ptqrtoken, qrsig);
            string res = await HttpPostAsync(check_url, content);
            Dictionary<string, string> re = new Dictionary<string, string>();
            if (res == null)return null;
            dynamic data = JsonConvert.DeserializeObject<dynamic> (res);

            bool isOK = data.isOk;
            int result = data.result;
            string message,uin, cookie;
            re.Add("isOK", isOK.ToString());
            if (isOK && result == 100)
            {
                message = data.message;
                uin = data.uin;

                var cookieBuilder = new StringBuilder();
                foreach (var item in data.cookie.Properties())
                {
                    cookieBuilder.Append($"{item.Name}={item.Value}; ");
                }
                cookie = cookieBuilder.ToString().TrimEnd(' ', ';');
                re.Add("message",message);
                re.Add("uin", uin);
                re.Add("cookie",cookie);
            }
            else
            {
                message = data.errMsg;
                re.Add("message", message);
            }

            return re;
        }
        public async Task<string> SetQQLoginCookies(string cookies)
        {
            // 设置QQ音乐的登录cookies
            string set_url = $"{addresss[1]}/user/setCookie";
            string content = @"{""data"": """ + cookies + @"""}";
            // string content = @"{""data"": ""Your actual JSON data here""}";
            string res = await HttpPostAsync(set_url, content);
            if (res == null)
            {
                return "Post返回数据为空";
            }
            dynamic data = JsonConvert.DeserializeObject<dynamic>(res);
            if(data.result == 100)
            {
                // 设置cookie完成
                string qqValue = ExtractValueFromCookie(cookies, "uin");
                // 设置cookie的QQ号
                string set_qq_url = $"{addresss[1]}/user/getCookie?id={qqValue}";
                await Task.Delay(500);
                res = await HttpGetAsync(set_qq_url);
                if (res == null)
                {
                    return "QQ:"+qqValue+"Get返回数据为空,检查格式是否正确";
                }
                data = JsonConvert.DeserializeObject<dynamic>(res);
                if(data.result == 100)
                {
                    // 设置QQ号完成
                    return "true";
                }
                else
                {
                    return "QQ:" + qqValue + "QQ号设置失败";
                }
            }
            else
            {
                return "cookie设置失败" ;
            }
        }
        public async Task<string> CheckQQCookie()
        {
            // 获取cookie,已失效
            string get_cookie_url = $"{addresss[1]}/user/cookie";
            string res = await HttpGetAsync(get_cookie_url);
            if(res == null)
                {
                return null;
            }
            JObject data = JObject.Parse(res);
            if (data["result"].ToString() =="100")
            {
                // 遍历全部"data"下的键值对
                JObject dataOjbect = (JObject)data["data"];
                StringBuilder cookie_string_builder = new StringBuilder();
                foreach (var cookie in dataOjbect)
                {
                    string key = cookie.Key;
                    JToken value = cookie.Value;
                    // 以key=value;形式保存
                    cookie_string_builder.AppendFormat("{0}={1};",cookie.Key,cookie.Value);
                }
                return cookie_string_builder.ToString();
            }
            else 
            {
                return null;
            }
        }

        //--------------------------获取歌曲--------------------------  
        public async Task<string> GetSongUrl(string Song_id, int type_music, string mediamid = "")
        {// 仅QQ音乐会需要mediamid
            // 获取歌曲URL
            if (type_music == 0)
            {
                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    // 网易云音乐
                    string url = $"{addresss[type_music]}/song/url/v1?id={Song_id}&level=exhigh";
                    if (cookies[type_music] != "")
                    {
                        url += $"&cookie={cookies[type_music]}";
                    }
                    string Json_get;
                    try
                    {// http get错误
                        Json_get = await HttpGetAsync(url);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    int code = jsonObject["code"]?.Value<int>() ?? 0;
                    if (code != 200)
                    {// code不为200
                        string errorMsg = jsonObject["message"]?.Value<string>() ?? "Unknown error";
                        throw new InvalidOperationException($"{error_http_error}-code:{code}: {errorMsg}-{url}");
                    }
                    // 解析数据
                    try
                    {
                        string Songurl = jsonObject["data"][0]["url"].ToString();
                        return Songurl;
                    }
                    catch (Exception)
                    {// JSON解析错误
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
            else if (type_music == 1)
            {
                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    // QQ音乐
                    string url = $"{addresss[type_music]}/song/url?id={Song_id}&mediaId={mediamid}";
                    string Json_get;
                    try
                    {// http get
                        Json_get = await HttpGetWithCookiesAsync(url, type_music);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    // 解析JSON
                    try
                    {
                        string Songurl = "";
                        Songurl = jsonObject["data"].ToString();
                        return Songurl;
                    }
                    catch (Exception)
                    {
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
            return null;
        }
        public async Task<Dictionary<string, string>> GetSongDetail(string Song_id, int type_music)
        {
            // 获取歌曲详细信息 返回值包括 "name":<歌曲名>, "picurl":<封面url>
            if (type_music == 0)
            {
                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    // 网易云音乐
                    string url = $"{addresss[type_music]}/song/detail?ids={Song_id}";
                    string Json_get;
                    try
                    {// http get错误
                        Json_get = await HttpGetAsync(url);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    int code = jsonObject["code"]?.Value<int>() ?? 0;
                    if (code != 200)
                    {// code不为200
                        string errorMsg = jsonObject["message"]?.Value<string>() ?? "Unknown error";
                        throw new InvalidOperationException($"{error_http_error}-code:{code}: {errorMsg}-{url}");
                    }
                    // 解析数据
                    Dictionary<string, string> re = new Dictionary<string, string>();
                    try
                    {
                        string name = jsonObject["songs"][0]["name"].ToString();
                        string author = jsonObject["songs"][0]["ar"][0]["name"].ToString();
                        string picurl = jsonObject["songs"][0]["al"]["picUrl"].ToString();
                        re.Add("name", name);
                        re.Add("author", author);
                        re.Add("picurl", picurl);
                        return re;
                    }
                    catch (Exception)
                    {// JSON解析错误
                        if(trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
            else if (type_music == 1)
            {
                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    string url = $"{addresss[1]}/song?songmid={Song_id}";
                    string Json_get;
                    try
                    {// http get错误
                        Json_get = await HttpGetWithCookiesAsync(url, type_music);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    Dictionary<string, string> re = new Dictionary<string, string>();
                    // 解析JSON
                    try
                    {
                        string name = jsonObject["data"]["track_info"]["name"].ToString();
                        string author = jsonObject["data"]["track_info"]["singer"][0]["name"].ToString();
                        string pmid = jsonObject["data"]["track_info"]["album"]["pmid"].ToString();
                        string picurl = $"https://y.gtimg.cn/music/photo_new/T002R300x300M000{pmid}.jpg";
                        re.Add("name", name);
                        re.Add("author", author);
                        re.Add("picurl", picurl);
                        // 新增media_mid，用于url申请，可能会防止空url的出现
                        string mediamid = jsonObject["data"]?["track_info"]?["file"]?["media_mid"]?.ToString() ?? Song_id;
                        re.Add("mediamid",mediamid);
                        return re;
                    }
                    catch (Exception)
                    {
                        if(trytime == maxtry -1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
            else
            {
                return null;
            }
        }
        public async Task<Dictionary<string, string>> SearchSong(string Song_name, int type_music)
        {
            // 查找歌曲, 返回类型为id或者错误 0
            Dictionary<string, string> re = new Dictionary<string, string>();
            if (type_music == 0)
            {
                for(int trytime = 0; trytime < maxtry; trytime++)
                {
                    // 网易云音乐
                    string url = $"{addresss[type_music]}/search?keywords={Song_name}&limit=1";
                    string Json_get;
                    try
                    {
                        Json_get = await HttpGetAsync(url);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    int code = jsonObject["code"]?.Value<int>() ?? 0;
                    if (code != 200)
                    {// code不为200
                        string errorMsg = jsonObject["message"]?.Value<string>() ?? "Unknown error";
                        throw new InvalidOperationException($"{error_http_error}-code:{code}: {errorMsg}-{url}");
                    }
                    try
                    {
                        string id_get = jsonObject["result"]["songs"][0]["id"].ToString();
                        string songname = jsonObject["result"]["songs"][0]["name"].ToString();
                        string authorname = jsonObject["result"]["songs"][0]["artists"][0]["name"].ToString();
                        re.Add("id",id_get);
                        re.Add("name",songname);
                        re.Add("author", authorname);
                        return re;
                    }
                    catch (Exception)
                    {
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
            else if (type_music == 1)
            {
                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    // QQ音乐
                    string url = $"{addresss[type_music]}/search?key={Song_name}&pageSize=1";
                    string Json_get;
                    try
                    {
                        Json_get = await HttpGetWithCookiesAsync(url,type_music);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    try
                    {
                        string id_get = jsonObject["data"]["list"][0]["songmid"].ToString();
                        string songname = jsonObject["data"]["list"][0]["songname"].ToString();
                        string authorname = jsonObject["data"]["list"][0]["singer"][0]["name"].ToString();
                        re.Add("id", id_get);
                        re.Add("name", songname);
                        re.Add("author", authorname);
                        return re;
                    }
                    catch (Exception)
                    {
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
            }
            return null;
        }
        public async Task<List<Dictionary<TimeSpan, string>>> GetSongLyric(string Song_id, int type_music)
        {
            List<Dictionary<TimeSpan, string>>  lyric = new List<Dictionary<TimeSpan, string>>();
            if (type_music == 0)
            {
                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    // 网易云音乐
                    string url = $"{addresss[type_music]}/lyric?id={Song_id}";
                    string Json_get;
                    try
                    {// http get错误
                        Json_get = await HttpGetAsync(url);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    int code = jsonObject["code"]?.Value<int>() ?? 0;
                    if (code != 200)
                    {// code不为200
                        string errorMsg = jsonObject["message"]?.Value<string>() ?? "Unknown error";
                        throw new InvalidOperationException($"{error_http_error}-code:{code}: {errorMsg}-{url}");
                    }
                    // 解析数据
                    try
                    {
                        string lyric_all;
                        /*
                        if (jsonObject["tlyric"]["lyric"].ToString() != "")
                        {// 若有翻译获取翻译
                            lyric_all = jsonObject["tlyric"]["lyric"].ToString();
                        }
                        else
                        {
                            lyric_all = jsonObject["lrc"]["lyric"].ToString();
                        }
                        */
                        lyric_all = jsonObject["lrc"]["lyric"].ToString();
                        // 格式转化
                        string[] lines = lyric_all.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            int closingBracketIndex = line.IndexOf(']');
                            if (closingBracketIndex <= 0) continue;

                            string timePart = line.Substring(1, closingBracketIndex - 1);
                            string lyric_single = line.Substring(closingBracketIndex + 1).Trim();

                            if (string.IsNullOrWhiteSpace(lyric_single)) continue;
                            // 解析
                            string[] timeFormats = new string[]
                            {
                                @"mm\:ss\.fff", // [11.11.111]
                                @"mm\:ss\.ff",  // [11.11.11]
                                @"mm\:ss\.f"    // [11.11.1]
                            };
                            if (TimeSpan.TryParseExact(timePart, timeFormats, CultureInfo.InvariantCulture, out TimeSpan timestamp))
                            {
                                var lyricEntry = new Dictionary<TimeSpan, string>();
                                lyricEntry.Add(timestamp, lyric_single);
                                lyric.Add(lyricEntry);
                            }
                            else
                            {
                                Console.WriteLine($"无法解析时间格式: {timePart}");
                            }
                        }
                        return lyric;
                    }
                    catch (Exception)
                    {// JSON解析错误
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
            else if (type_music == 1)
            {

                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    // QQ音乐
                    string url = $"{addresss[type_music]}/lyric?songmid={Song_id}";
                    string Json_get;
                    try
                    {// http get错误
                        Json_get = await HttpGetAsync(url);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    // 解析数据
                    try
                    {
                        string lyric_all;
                        /*
                        if (jsonObject["data"]["trans"].ToString() != "")
                        {// 若有翻译获取翻译
                            lyric_all = jsonObject["data"]["trans"].ToString();
                        }
                        else
                        {
                            lyric_all = jsonObject["data"]["lyric"].ToString();
                        }
                        */
                        lyric_all = jsonObject["data"]["lyric"].ToString();
                        // 格式转化
                        string[] lines = lyric_all.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            Match match = Regex.Match(line, @"^\[(\d{2}:\d{2}\.\d{1,3})\](.*)$");
                            if (match.Success)
                            {
                                string timeStr = match.Groups[1].Value;
                                string lyric_single = match.Groups[2].Value.Trim();
                                string[] timeFormats = new string[]
                                {
                                    @"mm\:ss\.fff", // [11.11.111]
                                    @"mm\:ss\.ff",  // [11.11.11]
                                    @"mm\:ss\.f"    // [11.11.1]
                                };
                                if (TimeSpan.TryParseExact(timeStr, timeFormats, CultureInfo.InvariantCulture, out TimeSpan time))
                                {
                                    Dictionary<TimeSpan, string> entry = new Dictionary<TimeSpan, string>();
                                    entry.Add(time, lyric_single);
                                    lyric.Add(entry);
                                }
                            }
                        }
                        return lyric;
                    }
                    catch (Exception)
                    {// JSON解析错误
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
             return null;
        }
        public async Task<String> GetmidFromId(string id)
        {// 把id转为mid
            string mid = "";
            string res = await HttpGetWithCookiesAsync($"https://y.qq.com/n/ryqq/songDetail/{id}", 1);
            // 开始处理
            string[] afterequal = res.Split("__INITIAL_DATA__ =");
            if (afterequal.Length != 2)
            {
                throw new Exception("分隔__INITIAL_DATA__ =出现错误");
            }
            string[] beforescipt = afterequal[1].Split("</script>");
            
            if (beforescipt.Length == 1)
            {
                throw new Exception("分隔</script>出现错误");
            }
            string json_get = beforescipt[0];
            // 解析json
            try
            {
                var jsonObject = JObject.Parse(json_get);
                mid = jsonObject["songList"][0]["mid"].ToString();
            }
            catch (Exception)
            {
                throw new Exception($"Json解析错误-{json_get}");
            }
            return mid;
        }
        //-------------------------获取歌单--------------------------
        public async Task<long> SearchPlayList(string PlayList_name, int type_music)
        {
            if (type_music == 0) {
                for(int trytime = 0; trytime< maxtry; trytime++)
                {
                    // 网易云音乐歌单搜索
                    string url = $"{addresss[type_music]}/search?keywords={PlayList_name}&limit=1&type=1000";
                    string Json_get;
                    try
                    {// http get错误
                        Json_get = await HttpGetAsync(url);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    int code = jsonObject["code"]?.Value<int>() ?? 0;
                    if (code != 200)
                    {// code不为200
                        string errorMsg = jsonObject["message"]?.Value<string>() ?? "Unknown error";
                        throw new InvalidOperationException($"{error_http_error}-code:{code}: {errorMsg}-{url}");
                    }
                    try
                    {// 解析数据
                        string id_get = jsonObject["result"]["playlists"][0]["id"].ToString();
                        long id = long.Parse(id_get);
                        return id;
                    }
                    catch (Exception)
                    {
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return 0;
            }
            else if(type_music == 1)
            {
                for (int trytime = 0; trytime < maxtry; trytime++)
                {
                    // QQ音乐
                    string url = $"{addresss[1]}/search?key={PlayList_name}&t=2";
                    string Json_get;
                    try
                    {
                        Json_get = await HttpGetWithCookiesAsync(url, type_music);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    try
                    {
                        string id_get = jsonObject["data"]["list"][0]["dissid"].ToString();
                        long id = long.Parse (id_get);
                        return id;
                    }
                    catch (Exception)
                    {
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return 0;
            }
            return 0;
        }
        public async Task<List<Dictionary<string,string>>> GetPlayListDetail(string PlayList_id, int type_music)
        {
            // 获取歌单 返回数据结构[ {id:id,name:name,author},{id:id,name:name,author}]
            List< Dictionary<string, string> > re = new List < Dictionary<string, string> >();
            if (type_music == 0)
            {
                string url = $"{addresss[type_music]}/playlist/detail?id={PlayList_id}";
                if (cookies[type_music] != "")
                {
                    url += $"&cookie={cookies[type_music]}";
                }
                string Json_get = await HttpGetAsync(url);
                if (Json_get == null)
                {
                    return null;
                }
                var jsonObject = JObject.Parse(Json_get);
                // 歌单信息获取完成
                int code = (int)jsonObject["code"];
                int song_count = (int)jsonObject["playlist"]["trackCount"];

                if (code == 200)
                {
                    for (int i = 0; i < song_count / 50 + 1; i++)
                    {
                        // 每次循环获得50首，若剩余小于50则取剩余数
                        for (int j = 0; j < ((song_count - i * 50) < 50 ? song_count - i * 50 : 50); j++)
                        {
                            // 获取i*50到i*50+50之间
                            string tracks_url = $"{addresss[type_music]}/playlist/track/all?id={PlayList_id}&limit=50&offset={i * 50}";
                            if (cookies[type_music] != "")
                            {
                                tracks_url += $"&cookie={cookies[type_music]}";
                            }
                            string tracks_Json_get = await HttpGetAsync(tracks_url);

                            if (tracks_Json_get == null)
                            {
                                return null;
                            }
                            dynamic tracks_data = JsonConvert.DeserializeObject<dynamic>(tracks_Json_get);
                            // i*50到i*50+50获取完成
                            long song_id = tracks_data.songs[j].id;
                            string song_name = tracks_data.songs[j].name;
                            string song_author = tracks_data.songs[j].ar[0].name;

                            re.Add(new Dictionary<string, string> { { "id", song_id.ToString() }, { "name", song_name }, { "author", song_author } });
                        }
                    }
                    return re;
                }
            }
            else if (type_music == 1)
            {
                for(int trytime = 0; trytime <maxtry;  trytime++)
                {
                    // QQ音乐
                    string url = $"{addresss[1]}/songlist?id={PlayList_id}";
                    string Json_get;
                    try
                    {// http get错误
                        Json_get = await HttpGetAsync(url);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                    }
                    var jsonObject = JObject.Parse(Json_get);
                    try
                    {// 解析json
                        int song_count = (int)jsonObject["data"]["songnum"];
                        for (int i = 0; i< song_count; i++)
                        {
                            string song_mid = jsonObject["data"]["songlist"][i]["songmid"].ToString();
                            string song_name = jsonObject["data"]["songlist"][i]["songname"].ToString();
                            string song_author = jsonObject["data"]["songlist"][i]["singer"][0]["name"].ToString();
                            re.Add(new Dictionary<string, string> { { "id", song_mid }, { "name", song_name }, { "author", song_author } });
                        }
                        return re;
                    }
                    catch (Exception)
                    {
                        if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                        await Task.Delay(trydelay);
                    }
                }
                return null;
            }
            return null;
        }
        //-------------------------FM--------------------------
        public async Task<string> GetFMSongId()
        {
            string url = $"{addresss[0]}/personal/fm/mode?mode=DEFAULT&timestamp={GetTimeStamp()}";
            if (cookies[0] != "")
            {
                url += $"&cookie={cookies[0]}";
            }
            for(int trytime = 0; trytime < maxtry; trytime++)
            {
                string Json_get;
                try
                {// http get错误
                    Json_get = await HttpGetAsync(url);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException($"{error_http_error}-msg:{e.Message}-{url}");
                }
                var jsonObject = JObject.Parse(Json_get);
                int code = jsonObject["code"]?.Value<int>() ?? 0;
                if (code != 200)
                {// code不为200
                    string errorMsg = jsonObject["message"]?.Value<string>() ?? "Unknown error";
                    throw new InvalidOperationException($"{error_http_error}-code:{code}: {errorMsg}-{url}");
                }
                string id=null;
                try
                {
                    id = jsonObject["data"][0]["id"].ToString();
                    return id;
                }
                catch (Exception)
                {
                    if (trytime == maxtry - 1) { throw new ArgumentException($"{error_json_error}-{Json_get}"); }
                    await Task.Delay(800);
                }
            }
            return null ;
           
        }

        //--------------------------HTTP相关--------------------------
        public static async Task<string> HttpGetAsync(string url)
        {
            using HttpClient client = new HttpClient();
            try
            {
                // 发送GET请求
                HttpResponseMessage res = await client.GetAsync(url);
                // 确保HTTP成功状态值
                res.EnsureSuccessStatusCode();
                // 读取响应内容为字符串
                string responseBody = await res.Content.ReadAsStringAsync();
                return responseBody;
            }
            catch (HttpRequestException e)
            {
                throw e;
            }
        }
        public async Task<string> HttpGetWithCookiesAsync(string url, int type_music)
        {
            using (var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer()
            })
            {
                // 从类成员获取cookie字符串
                string cookieString = this.cookies[type_music];

                if (!string.IsNullOrEmpty(cookieString))
                {
                    // 分割cookie字符串为键值对
                    string[] cookiePairs = cookieString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var pair in cookiePairs)
                    {
                        int equalsIndex = pair.IndexOf('=');
                        if (equalsIndex > 0)
                        {
                            string key = pair.Substring(0, equalsIndex).Trim();
                            string value = pair.Substring(equalsIndex + 1).Trim();
                            try
                            {
                                // 将cookie添加到CookieContainer
                                handler.CookieContainer.Add(new Uri(url), new Cookie(key, value));
                            }
                            catch (UriFormatException)
                            {
                                Console.WriteLine("Invalid URL format for cookie domain");
                            }
                        }
                    }
                }

                using (var client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                    try
                    {
                        HttpResponseMessage response = await client.GetAsync(url);
                        response.EnsureSuccessStatusCode();
                        var cookies = handler.CookieContainer.GetCookies(new Uri(url));
                        // Console.WriteLine($"Sent cookies: {string.Join(", ", cookies.Cast<Cookie>().Select(c => $"{c.Name}={c.Value}"))}");
                        return await response.Content.ReadAsStringAsync();
                    }
                    catch (HttpRequestException e)
                    {
                        throw e;
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }
            }
        }
        public static async Task<string> HttpPostAsync(string url, string jsonData)
        {
            using (var client = new HttpClient())
            {
                HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                try
                {
                    // 发起POST请求
                    HttpResponseMessage response = await client.PostAsync(url, content);
                    // 确保请求成功
                    response.EnsureSuccessStatusCode();
                    // 读取响应内容
                    string responseBody = await response.Content.ReadAsStringAsync();
                    return responseBody;
                }
                catch (HttpRequestException e)
                {
                    // 异常处理
                    throw e;
                }
            }
        }
        public async Task<byte[]> HttpGetImage(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";

            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
            using (Stream stream = response.GetResponseStream())
            using (MemoryStream memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                return memoryStream.ToArray();
            }
        }
        //--------------------------其他功能性--------------------------
        public static string GetTimeStamp()
        {
            //获得时间戳
            TimeSpan ts = DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            return Convert.ToInt64(ts.TotalSeconds).ToString();
        }
        public static string ExtractValueFromCookie(string cookie, string fieldName)
        {
            string pattern = fieldName + @"=(\d+)";
            Match match = Regex.Match(cookie, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            else
            {
                return null;
            }
        }
    }
}

