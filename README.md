# TS3AudioBot-Plugin-Netease-QQ
>基于Splamy/TS3AudioBot项目 https://github.com/Splamy/TS3AudioBot
>基于网易云音乐API项目 https://github.com/Binaryify/neteasecloudmusicapi
>网易云音乐API文档 https://binaryify.github.io/NeteaseCloudMusicApi/#/
>基于QQ音乐API项目 https://github.com/jsososo/QQMusicApi
>QQ音乐API文档 https://qq-api-soso.vercel.app/#/

参考了[ZHANGTIANYAO1](https://github.com/ZHANGTIANYAO1)的[TS3AudioBot-NetEaseCloudmusic-plugin](https://github.com/ZHANGTIANYAO1/TS3AudioBot-NetEaseCloudmusic-plugin)项目
参考了[FiveHair](https://github.com/FiveHair)的[TS3AudioBot-NetEaseCloudmusic-plugin-UNM](https://github.com/FiveHair/TS3AudioBot-NetEaseCloudmusic-plugin-UNM)项目

TeamSpeak3音乐机器人插件，实现在语音频道中播放网络QQ音乐和网易云音乐。
需要自行部署1. TS3AudioBot  2.网易云API 3.QQ音乐API （API也可只部署其中一个）
只测试过在Docker环境下能正常使用，理论Windows和Linux同样支持，推荐使用docker。

## 功能介绍
- 支持QQ音乐和网易云音乐扫码登录
- 指令点歌，使用`!bgm play xxx`指令播放，使用`!qq play xxx`可以播放QQ音乐里的歌曲，同时支持输入ID和歌名查询（建议输入ID）。
- 支持播放模式切换，使用`!bgm mode xxx`来切换，支持[0=顺序播放 1=单曲循环 2=顺序循环 3=随机播放]。
- 支持网易云FM模式，使用`!wyy fm`进入fm播放模式，进入后使用`!bgm next`进入下一首歌
- 内置歌曲列表，使用`!bgm ls`查看当前的播放歌曲列表，使用`!bgm go xxx`跳转歌曲，使用`!bgm rm xxx`来删除歌曲，使用`!bgm clear`来清除列表。
- 支持歌词功能，使用`!bgm lyric`开启，默认关闭，歌词会显示在机器人的简介。
- 频道无人超出30s自动停止播放。

## 基本使用方法
您需要部署TS3AudioBot，网易云API，QQ音乐API。
1. 安装插件，将[**TS3AudioBot-Plugin-Netease-QQ.dll**](https://github.com/RayQuantum/TS3AudioBot-Plugin-Netease-QQ/releases/download/v1.2.0/TS3AudioBot-Plugin-Netease-QQ.dll)文件以及配置文件netease_qq_config.ini复制到TS3AudioBot的/plugins文件夹下，如果没有请自行创建插件文件夹，文件的目录应该如下：
```
- Bots
  - default
    - bot.toml
- logs
- plugins
  - TS3AudioBot-Plugin-Netease-QQ.dll
  - netease_qq_config.ini
- NLog.config
- rights.toml
- ts3audiobot.db
- ts3audiobot.toml
```
2. 设置配置文件，编辑文件netease_qq_config.ini，默认的配置文件应该如下，在neteaseAPI和qqAPI处分别填入自己的api接口地址。随后将自己的QQ和网易云（可选）的cookies填入，保存。
```
[netease]
neteaseAPI = http://127.0.0.1:3000
cookies = ""

[qq]
qqAPI = http://127.0.0.1:3300
cookies = ""
```
3. 启用插件，在TS3服务器中和机器人私聊，输入`!plugin list`，你会看到输出`#0|RDY|Netease_QQ_plugin (BotPlugin)`，随后输入`!plugin load 0`（具体序号看自己的输出），结果为`#0|+ON|Netease_QQ_plugin (BotPlugin)`则成功启用了插件。
4. 添加频道描述
5. 输入指令`!wyy login`，此时查看音乐机器人的头像，变成了二维码，扫码后登录成功，即可开始使用。
后续是使用docker的详细部署过程

### 频道描述（复制到频道说明）
```
[COLOR=#0000ff]私聊机器人，指令如下(把[xxx]替换成对应信息，删除中括号)[/COLOR]
基本操作
1.播放音乐(默认网易云)
[COLOR=#0000ff]!bgm play [音乐id或者名称][/COLOR]
2.暂停音乐(或继续)
[COLOR=#0000ff]!pause[/COLOR]
3.修改音量
[COLOR=#0000ff]!volume [0-100的音量][/COLOR]
4.下一首
[COLOR=#0000ff]!bgm next[/COLOR]
5.上一首
[COLOR=#0000ff]!bgm pre[/COLOR]
6.改变播放模式[0=顺序播放 1=单曲循环 2=顺序循环 3=随机循环]
[COLOR=#0000ff]!bgm mode [模式号][/COLOR]
7.列出歌曲列表(默认第一页)
[COLOR=#0000ff]!bgm ls[/COLOR]
8.跳转到歌单中的歌曲
[COLOR=#0000ff]!bgm go [音乐索引][/COLOR]
9.从列表移除歌曲
[COLOR=#0000ff]!bgm rm [音乐索引][/COLOR]
10.列出歌曲列表第N页
[COLOR=#0000ff]!bgm ls p [第N页][/COLOR]
11.清空歌曲列表
[COLOR=#0000ff]!bgm clear[/COLOR]
12.启用歌词功能
[COLOR=#0000ff]!bgm lyric[/COLOR]
-------------------------------------------
网易云：
1.登录网易云账号(输入后通过机器人头像扫码登录)
[COLOR=#ff0000]!wyy login[/COLOR]
2.立即播放网易云音乐(只播放这首)
[COLOR=#ff0000]!wyy play [音乐id或者名称][/COLOR]
3.添加音乐到下一首
[COLOR=#ff0000]!wyy add [音乐id或者名称][/COLOR]
4.播放网易云音乐歌单
[COLOR=#ff0000]!wyy gd [歌单id或者名称][/COLOR]
5.进入网易云FM模式(在FM模式中，下一首则为next)
[COLOR=#ff0000]!wyy fm[/COLOR]
-------------------------------------------
QQ音乐
1.登录qq账号(使用QQ扫码登录)
[COLOR=#0eb050]!qq login[/COLOR]
2.使用cookie登录qq账号(使用cookie登录)
[COLOR=#0eb050]!qq cookie [填入cookie][/COLOR]
3.立即播放QQ音乐(QQ音乐id是带字母的)
[COLOR=#0eb050]!qq play [音乐id或者名称][/COLOR]
4.添加音乐到下一首
[COLOR=#0eb050]!qq add [音乐id或者名称][/COLOR]
5.播放QQ音乐歌单
[COLOR=#0eb050]!qq gd [歌单id或者名称][/COLOR]
6.加载本地的QQ的cookie
[COLOR=#0eb050]!qq load[/COLOR]
需要注意的是如果歌单歌曲过多需要时间加载
以下例子加粗的就是音乐或者歌单id
网易云歌：https://music.163.com/#/song?id=[B]254548[/B]
网易云歌单：https://music.163.com/#/playlist?id=[B]545016340[/B]
QQ歌：https://y.qq.com/n/ryqq/songDetail/[B]004Z8Ihr0JIu5s[/B]
QQ歌单：https://y.qq.com/n/ryqq/playlist/[B]3805603854[/B]
```

## 使用docker从零部署音乐机器人
使用三个docker实现部署机器人
- [TS3AudioBot_docker](https://github.com/getdrunkonmovies-com/TS3AudioBot_docker)
- [网易云API Docker安装](https://binaryify.github.io/NeteaseCloudMusicApi/#/?id=docker-%e5%ae%b9%e5%99%a8%e8%bf%90%e8%a1%8c)
- [QQmusicAPI_dockerImage](https://github.com/RayQuantum/QQmusicAPI_docker_Image)

### 1. 使用docker部署TS3AudioBot
原始项目来源[TS3AudioBot](https://github.com/Splamy/TS3AudioBot)，推荐使用docker版部署[TS3AudioBot_docker](https://github.com/getdrunkonmovies-com/TS3AudioBot_docker)，这里把安装教程翻译了一下。推荐先拉取镜像`ancieque/ts3audiobot:0.12.0`。
1. 创建默认文件夹，先找到一个放项目的文件夹这里假设是`/home/ray/ts3bot/data`，创建后将其分配给9999用户，因为docker中的用户是9999，不修改的话会导致读取不了文件。
```
mkdir -p /home/ray/ts3bot/data
chown -R 9999:9999 /home/ray/ts3bot/data
```
2. 初始运行一次，以创建初始配置文件，运行一次然后按CTRL-C停止，创建好了默认文件夹。
```
docker run --rm --mount type=bind,source="/home/ray/ts3bot/data",target=/app/data -it ancieque/ts3audiobot:0.12.0
```
3. 给自己添加TS3AudioBot的管理员权限，先打开teamspeak，点击工具->身份，查看自己的UID，修改/data下的rights.toml文件，把自己的UID填入useruid中，若需要更多管理员则加逗号继续添加。
```
# Admin rule
[[rule]]
    # Set your admin Group Ids here, ex: [ 13, 42 ]
    groupid = []
    # And/Or your admin Client Uids here
    useruid = ["此处填入自己的UID"]
    # By default treat requests from localhost as admin
    ip = [ "127.0.0.1", "::1" ]
    "+" = "*"
```
4. 给所有用户添加使用指令的权限，依然是rights.toml，删掉原本的groupid和useruid，即可给全部用户权限，保存。
```
# Playing rights
[[rule]]
    # Set Group Ids you want to allow here, ex: [ 13, 42 ]
	# And/Or Client Uids here, ex [ "uA0U7t4PBxdJ5TLnarsOHQh4/tY=", "8CnUQzwT/d9nHNeUaed0RPsDxxk=" ]
	# Or remove groupid and useruid to allow for everyone
    "+" = [
        # bgm
        "cmd.bgm.play",
        "cmd.bgm.next",
        "cmd.bgm.pre",
        "cmd.bgm.mode",
        "cmd.bgm.ls",
        "cmd.bgm.go",
        "cmd.bgm.rm",
        "cmd.bgm.ls.p",
        "cmd.bgm.clear",
        "cmd.bgm.lyric",
        # wyy
        "cmd.wyy.login",
        "cmd.wyy.play",
        "cmd.wyy.add",
        "cmd.wyy.gd",
        "cmd.wyy.fm",
        # qq
        "cmd.qq.login",
        "cmd.qq.cookie",
        "cmd.qq.play",
        "cmd.qq.add",
        "cmd.qq.gd",
        "cmd.qq.load",
        # Play controls
        "cmd.pause",
		"cmd.stop",
		"cmd.seek",
		"cmd.volume"
	]
```
5. 创建默认bot，这里是创建机器人的**关键步骤**，由于docker部署原因，不会自动创建bot，所以在bots文件夹里创建default文件夹，在default文件夹内创建bot.toml，填入自己的服务器，以及密码，频道密码，以及修改机器人名字。该部分教程来自于[TS3AudioBot_docker](https://github.com/getdrunkonmovies-com/TS3AudioBot_docker)项目的[issues1](https://github.com/getdrunkonmovies-com/TS3AudioBot_docker/issues/1)，在这部分没有经过测试。以下的中文是需要修改的地方。ps:<teamspeak 3 identity>部分我不清楚如何修改，可能可以通过Windows版本的ts3audiobot手动创建一个机器人，然后把其中的identity复制过来，似乎offset也需要修改，或者直接从其他部署方式得到的bot.toml继承下来。
```toml
#Starts the instance when the TS3AudioBot is launched.
run = true

[bot]

[commands]

[commands.alias]
default = "!play <url>"
yt = "!search from youtube (!param 0)"

[connect]
#The server password. Leave empty for none.
server_password = { pw = "服务器密码" }
#The default channel password. Leave empty for none.
channel_password = {  }
#Overrides the displayed version for the ts3 client. Leave empty for default.
client_version = {  }
#The address, ip or nickname (and port; default: 9987) of the TeamSpeak3 server
address = "服务器IP,示例127.0.0.1:9987"
#Client nickname when connecting.
name = "机器人名字"
#Default channel when connecting. Use a channel path or "/<id>".
#Examples: "Home/Lobby", "/5", "Home/Afk \\/ Not Here".
channel = "<starting channel name>"

[connect.identity]
#||| DO NOT MAKE THIS KEY PUBLIC ||| The client identity. You can import a teamspeak3 identity here too.
key = "<需要修改teamspeak 3 identity>"
#The client identity offset determining the security level.
offset = 28

[reconnect]

[audio]
#When a new song starts the volume will be trimmed to between min and max.
#When the current volume already is between min and max nothing will happen.
#To completely or partially disable this feature, set min to 0 and/or max to 100.
volume = {  }

[playlists]

[history]

[events]
```
6. 创建plugins文件夹和加入插件，在bots同级目录创建plugins文件夹，把文件TS3AudioBot-Plugin-Netease-QQ.dll和netease_qq_config.ini添加到plugins文件夹内。
7. 文件创建部分到这里就结束了，最后推荐把文件用户重新分配一次，以确保这些容器有权限读取这些文件
```
chown -R 9999:9999 /home/ray/ts3bot/data
```
8. 创建容器，映射端口58913。
```
docker run --name ts3audiobot -d --mount type=bind,source="$(pwd)/data",target=/app/data -p 58913:58913 ancieque/ts3audiobot:0.12.0
```
9. 创建成功后，在teamspeak服务器应该已经连接了你的机器人了，随后和机器人私信，发送指令`!api token`，机器人会给你发送你的token。请注意，这里给你发送的过程可能会出现特殊的符号，导致被识别成表情，请手动替换一下，具体替换方法在聊天框把这个表情打出来，就知道这个表情代表的是什么字符。
10. 获取了token后，我们就可以登录ts3后台了，管理后台是http://127.0.0.1:58913，输入自己的uid和token，即可登录后台。
11. 启用插件，在TS3服务器中和机器人私聊，输入`!plugin list`，你会看到输出`#0|RDY|Netease_QQ_plugin (BotPlugin)`，随后输入`!plugin load 0`（具体序号看自己的输出），结果为`#0|+ON|Netease_QQ_plugin (BotPlugin)`则成功启用了插件。
12. 添加频道描述
到此TS3音乐机器人安装完成。

### 2.部署网易云API
原文链接：https://binaryify.github.io/NeteaseCloudMusicApi
1. 部署容器
```
docker run -d -p 3000:3000 --name netease_cloud_music_api binaryify/netease_cloud_music_api
```
2. 尝试连接，登录网站http://127.0.0.1:3000，看到有网易云api的提示即安装完成

### 3.部署QQ音乐API
原项目：https://github.com/jsososo/QQMusicApi
分支项目：https://github.com/yunxiangjun/QQMusicApi/tree/master。（支持扫码登陆）
为了支持扫码登录和cookie保存，这里我自己修改了一下原项目的代码，重新打包了一个docker文件，之后的插件都需要使用我修改过的QQ音乐API才能正常使用。具体部署项目如下。
部署QQ音乐API由于没有官方的Docker镜像，所以这里我自己打包了一个上传在另外一个项目[QQmusicAPI_docker_Image](https://github.com/RayQuantum/QQmusicAPI_docker_Image)，文件较大，具体部署步骤可以查看该项目内。
下载链接：https://github.com/RayQuantum/QQmusicAPI_docker_Image/releases/download/v1.1.0/qqmusic_qr_image.tar
1. 下载后，执行`docker load qqmusic_qr_image.tar`
2. 部署容器
```
docker run -d -p 3300:3300 --name qqmusic_api qqmusicapi_qr
```
3. 尝试连接，登录网站http://127.0.0.1:3300，看到有QQ音乐api的提示即安装完成

## 写在最后
原本这个插件在24年6月就写完了，直到今天我才翻出来重新完善，由此插件还有很多bug，希望大家遇到问题可以提出。
