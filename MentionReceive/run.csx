#r "Microsoft.WindowsAzure.Storage"
#r "Newtonsoft.Json"
using System.Configuration;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json.Linq;

public static async Task Run(HttpRequestMessage req,
    IQueryable<TableEntity> inputUserTable,
    IAsyncCollector<TableEntity> outputUserTable,
    TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

    string reqContent = await req.Content.ReadAsStringAsync();
    log.Info(reqContent.ToString());

    // 認証:LINEからのPushであることを確認する
    string signatureKey = "X-Line-Signature";
    if (!req.Headers.Contains(signatureKey)) {
        // 認証エラー : 署名なし
        log.Error("LINEの署名がついていません");
        return;
    }
    string givenSignature = req.Headers.GetValues(signatureKey).First();

    string lineChannelSecret = ConfigurationManager.AppSettings["line_channel_secret"];

    using (HMACSHA256 hmacsha256 = new HMACSHA256(Encoding.UTF8.GetBytes(lineChannelSecret))) {
        byte[] hashValue = hmacsha256.ComputeHash(Encoding.UTF8.GetBytes(reqContent));
        string calcedSignature = Convert.ToBase64String(hashValue);

        if (givenSignature != calcedSignature) {
            // 認証エラー : 不正な署名
            log.Error("LINEの署名が不正です");
            return;
        }
    }

    // Parse
    string userId;
    try {
        var obj = JObject.Parse(reqContent);
        userId = obj["events"][0]["source"]["userId"].ToString(); // 雑だけどよしとする
    } catch (Exception e) {
        log.Error("リクエストからJSONのParse失敗");
        log.Error(e.ToString());
        return;
    }

    // 一致するUserIDを検索
    bool hasSameUserId = inputUserTable.Where(x => x.PartitionKey == "User")
                                       .Where(x => x.RowKey == userId)
                                       .FirstOrDefault()
                                       != null;

    // 一致するUserIDが存在しなければ、新規登録する
    if (!hasSameUserId) {
        log.Info("ID新規登録 開始");
        await outputUserTable.AddAsync(new TableEntity {
            PartitionKey = "User",
            RowKey = userId,
        });
        log.Info("ID新規登録 終了");
    } else {
        log.Info("既に登録されたUserIDです");
    }

    return ;
}
