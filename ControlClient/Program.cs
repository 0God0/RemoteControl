using System.Text;

class TcpClientExample {
    static void Main() {
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

        client.RegisterHandler("COMMAND", (_, marker, payload) => {
            Execute.
        });
            

        while (!client.Connect("127.0.0.1", 11451)) ;

        int i = 2;
        // 发送文本消息（加密）
        while (true) {
            if (i-- < 0) break; // 限制发送次数
            client.SendPacket("MSG", "Hello, server!", true);
            Thread.Sleep(1000); // 每秒发送一次
        }



        // 发送文件（不加密）
        //byte[] fileData = File.ReadAllBytes("large_file.dat");
        //client.SendBytes("FILE", fileData, false);

        client.Disconnect();
    }
}