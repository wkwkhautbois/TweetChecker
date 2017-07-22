# TweetChecker
## 概要

下記のことを行うサンプルです

MentionrRceive
- LINE@アカウントと友達になった、もしくは何かトークに書きこみがあったときのPush通知を広い、DBにLINEユーザーIDを登録する

TweetCheck
- 特定のTwitterIDから、指定したワードを含むツイートを検索し取得する(定期実行)
- 新しいツイートが増えていた場合、LINE@アカウントで登録ユーザーにPush通知する

## アーキテクチャ

必要アカウント
- LINE@アカウント
- Twitterアカウント(TwitterAPIのアクセストークンなどのため)
- Azureアカウント(Azure Functions, Azure Table Storage 利用のため)

