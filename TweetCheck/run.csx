#r "Microsoft.WindowsAzure.Storage"
#r "Newtonsoft.Json"
#r "System.Collections"
#r "System.Linq.Expressions"
#r "System.Web"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using System.Net.Http;
using LinqToTwitter;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System.Text;
using System.Web;

class Tweet : TableEntity
{
    public string Id { get; set; }
}

class Message
{
    public string type { get { return "text"; } }
    public string text { get; set; }
}

class LinePushMessage
{
    public string to { get; set; }
    public List<Message> messages { get; set; }
}

public static void Run(TimerInfo myTimer, TraceWriter log)
{
    log.Info($"C# Timer trigger function executed at: {DateTime.Now}");

    // AzureStorageとの接続 
    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
        ConfigurationManager.AppSettings["StorageConnectionString"]);
    
    // ====TableStorageのテーブルの使用準備====
    CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
    CloudTable searchTable = tableClient.GetTableReference("Search");
    CloudTable dataTable = tableClient.GetTableReference("Data");

    // ====Twitter検索ワード生成====
    TableQuery<TableEntity> selectWordQuery = new TableQuery<TableEntity>()
        .Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "Word"));

    string searchPhrase = searchTable.ExecuteQuery(selectWordQuery)
        .Select(x => x.RowKey)
        .Select(x => HttpUtility.UrlEncode(x)) // 各検索ワードをURLエンコード
        .Aggregate((x, y) => $"{x} OR {y}") // OR結合
        ;

    if (searchPhrase == "")
    {
        log.Error("検索ワードが取得できませんでした");
        return;
    }

    // ====Twitter最終検索時ID取得====
    TableOperation selectLastTweetOperation = TableOperation.Retrieve<Tweet>("Result", "LastTweet");

    Tweet lastTweet = (Tweet)searchTable.Execute(selectLastTweetOperation).Result;
    if(lastTweet == null)
    {
        // テーブルにツイート情報が存在しなかったら、ダミーデータを登録する
        lastTweet = new Tweet
        {
            PartitionKey = "Result",
            RowKey = "LastTweet",
            Id = "0"
        };
    }

    // ====Twitter検索====
    var auth = new LinqToTwitter.SingleUserAuthorizer
    {
        CredentialStore = new LinqToTwitter.SingleUserInMemoryCredentialStore
        {
            ConsumerKey = ConfigurationManager.AppSettings["TwitterConsumerKey"],
            ConsumerSecret = ConfigurationManager.AppSettings["TwitterConsumerSecret"],
            AccessToken = ConfigurationManager.AppSettings["TwitterAccessToken"],
            AccessTokenSecret = ConfigurationManager.AppSettings["TwitterAccessTokenSecret"]
        }
    };

    var twitterCtx = new TwitterContext(auth);

    string twitterId = "********"; //検索対象のTwitterID
    var r = twitterCtx.Search
                      .Where(x => x.Type == SearchType.Search
                          && x.Query == $"{searchPhrase} from:{twitterId}"
                          && x.IncludeEntities == true)
                      .Where(x => x.SinceID == ulong.Parse(lastTweet.Id))
                      .SingleOrDefault();

    if (r?.Statuses == null)
    {
        log.Error("検索失敗");
        return;
    }
    else if (r.Statuses.Count == 0)
    {
        log.Info("検索結果 0件");
        return;
    }


    // ====LineMessage送信先取得====
    TableQuery<TableEntity> selectLineIdQuery = new TableQuery<TableEntity>()
        .Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "User"));

    List<string> lineIds = dataTable.ExecuteQuery(selectLineIdQuery)
        .Select(x => x.RowKey)
        .ToList();


    // ====LineMessage送信(5件まで:それを超すことはないという前提)====
    List<Message> messages = r.Statuses
    .OrderBy(x => x.CreatedAt)
    .Select(x => $"{TimeZoneInfo.ConvertTimeFromUtc(x.CreatedAt, TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"))}\n{x.Text ?? x.Text}")
    .Select(x => new Message
    {
        text = x
    })
    .ToList();

    HttpClient httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ConfigurationManager.AppSettings["line_channel_token"]}");

    log.Info($"messages:{JsonConvert.SerializeObject(messages)}");

    foreach (string lineId in lineIds)
    {
        var sendMessage = JsonConvert.SerializeObject(new LinePushMessage()
        {
            to = lineId,
            messages = messages
        });

        log.Error(JsonConvert.SerializeObject(sendMessage));

        const string linePushUrl = "https://api.line.me/v2/bot/message/push";
        var linePushResponse = httpClient.PostAsync(linePushUrl, new StringContent(sendMessage, Encoding.UTF8, "application/json")).Result;
        if (linePushResponse.StatusCode != System.Net.HttpStatusCode.OK)
        {
            log.Error("LINEへのPUSHに失敗しました");
            
            log.Error($"ErrorMessage : {linePushResponse.Content.ReadAsStringAsync().Result}");
            log.Error($"sendMessage : {sendMessage}");
        }

        log.Info(lineId);
    }


    // ====ヒットしたTweetのIDを保存====
    lastTweet.Id = r.Statuses
        .OrderBy(x => x.CreatedAt)
        .First()
        .StatusID
        .ToString();
    TableOperation updateLastTweetOperation = TableOperation.InsertOrReplace(lastTweet);

    searchTable.Execute(updateLastTweetOperation);
}
