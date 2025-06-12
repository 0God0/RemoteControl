using NetWork;
using System.Text;

class TcpClientExample {
     public static async Task Main() {
        var client = new NetWork.TcpClient(ex => {
            Console.WriteLine($"Client error: {ex.Message}");

        });

        // 设置AES加密
        var key = Encoding.UTF8.GetBytes("1234567891234567");
        client.SetEncryption(
            data => CAes.AesEncrypt(data, key),
            data => CAes.AesDecrypt(data, key)
        );

        // 注册文本消息处理器
        client.RegisterHandler("REPLY", (_, marker, payload) =>
            Console.WriteLine($"Server reply: {payload}"));

        // 注册文件传输处理器
        client.RegisterBytesHandler("FILE", (_, marker, data) =>
            Console.WriteLine($"Received file: {data.Length} bytes"));
        while (!client.Connect("127.0.0.1", 11451)) ;

        int i = 10;
        // 发送文本消息（加密）
        //while (true) {
            client.SendPacket("MSG", "Hello, server!", true);
        //    Thread.Sleep(1000); // 每秒发送一次
        //}


        await client.SendFileAsync(
            path: @"D:\新建文本文档.txt", // 任意绝对或相对路径
            chunkSize: 128 * 1024,                 // 可选：调大提高吞吐
            encrypt: false);
        Thread.Sleep(10000); // 等待文件传输完成

        client.Disconnect();
    }
}