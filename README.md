# ArpPingScanner
CIDR形式で指定したIP範囲をPingでスキャンし、ホスト名、MACアドレス、到達可否を並列処理で効率的に取得するツールです。

## 前提条件
1. **.NET 8 デスクトップランタイムのインストール**  
   このツールを実行するには、.NET 8 デスクトップランタイムが必要です。以下のリンクからインストールしてください。  
   [.NET 8 ダウンロードページ](https://dotnet.microsoft.com/download/dotnet/8.0)

## 発行方法
開発者向けの手順です。ソースコードからツールを発行するには、以下のコマンドを実行してください。

```ps1
dotnet publish --configuration Release -p:PublishProfile=FolderProfile --output ".\artifacts" ".\src\ArpPingScanner\ArpPingScanner.csproj"
```

発行されたバイナリは、`artifacts` フォルダ内に出力されます。

## 使用方法
```ps1
.\artifacts\ArpPingScanner.exe --help
Description:
  指定したCIDR範囲のネットワークスキャンを実行し、結果を表示またはCSVに出力します。

Usage:
  ArpPingScanner [<cidr>] [options]

Arguments:
  <cidr>  スキャン対象のCIDR (例: 192.168.10.0/24) [default: 192.168.10.0/24]

Options:
  -o, --output <output>  出力先CSVファイルパス (省略可能)
  --version              Show version information
  -?, -h, --help         Show help and usage information
```

## ツール実行例
以下は、`.\artifacts\ArpPingScanner.exe`を使用した実行例です。

### 例1: IP範囲を指定してスキャン
```ps1
.\artifacts\ArpPingScanner.exe 192.168.1.0/24
```

### 例2: スキャン結果をCSVファイルに保存
```ps1
.\artifacts\ArpPingScanner.exe 192.168.1.0/24 --output result.csv
```
