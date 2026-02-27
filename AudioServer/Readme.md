整体工作流程
语音识别流程 (大屏 -> 后台)

物理世界: 用户对着大屏的麦克风说话。
NAudio (耳朵): 捕获麦克风的声音，并整理成标准的音频数据流。已经替换成windows自带的音频捕获功能（适应aot发布）。
Vosk (大脑): 接收 NAudio 的数据流，进行分析和识别，最终输出文字（例如 "查询天气"）。
https://alphacephei.com/vosk/models   下载 vosk-model-small-cn-0.22
你的程序: 拿到 "查询天气" 这段文字，进行业务处理。
语音播报流程 (后台 -> 大屏)

你的程序: 根据业务逻辑，生成一段需要播报的文字（例如 "今天天气晴朗"）。
piper /  System.Speech (嘴巴): 接收 "今天天气晴朗" 这段文字，将其转换成声音。
1、https://github.com/rhasspy/piper/releases  下载后端的 piper.exe
2、下载中文语音模型
zh_CN-huayan-medium.onnx (这是神经网络模型)
zh_CN-huayan-medium.onnx.json (这是模型的配置文件)
将这两个文件也放到刚刚的 piper 文件夹里。

物理世界: 服务器的扬声器将声音播放出来。


