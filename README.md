# TinyHttpSSE.DotNet
一个小而美的由C#实现的 Http1.1 SSE + Chunked 的工程，包含服务端与客户端实现

## SSE 简介
Server-Sent Events（SSE）是一种基于 HTTP 协议的服务器推送技术，它允许服务器以流的方式向客户端实时推送数据。与 WebSocket 等双向通信技术不同，SSE 专注于单向通信（从服务器到客户端），特别适合需要服务器主动推送数据的场景。

## 快速预览
- 以管理员身份运行 visual studio（监听SSE终结点需要管理员权限）
- 配置 TinyHttpSSEServer.Test,TinyHttpSSEClient.Test 为启动项目
- 启动运行，便可以看到以sse流打印的简介和实时报时

效果如图：
![fastpreview](./assets/FastPreview.gif)

## 快速使用
