// 简单的AES加密实现（实际应用中应使用更安全的方式）
using System.Security.Cryptography;
public class CAes {
    public static byte[] AesEncrypt(byte[] data, byte[] key) {
        using (Aes aes = Aes.Create()) { 
            aes.Key = key;
            aes.IV = new byte[16]; // 实际应用中应使用随机IV

            using (MemoryStream ms = new MemoryStream())
            using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write)) {
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();
                return ms.ToArray();
            }
        }
    }

    public static byte[] AesDecrypt(byte[] data, byte[] key) {
        using (Aes aes = Aes.Create()) {
            aes.Key = key;
            aes.IV = new byte[16]; // 实际应用中应存储IV

            using (MemoryStream ms = new MemoryStream())
            using (CryptoStream cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write)) {
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();
                return ms.ToArray();
            }
        }
    }
}
