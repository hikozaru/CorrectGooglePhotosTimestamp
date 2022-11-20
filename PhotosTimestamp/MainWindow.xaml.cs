using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;

namespace PhotosTimestamp
{
    [DataContract]
    public class ImageInfo
    {
        [DataMember(Name = "photoTakenTime")]
        public PhotoTakenTime PhotoTakenTime { get; set; }
    }
    [DataContract]
    public class PhotoTakenTime
    {
        [DataMember(Name = "timestamp")]
        public string timstamp { get; set; }
    }

    public partial class MainWindow : Window
    {
        enum E_RESULT
        {
            更新,
            失敗,
            SKIP
        };

        class Result
        {
            public int 番号 { get; set; }
            public E_RESULT 結果 { get; set; }
            public string ファイル名 { get; set; }
            public string エラーメッセージ { get; set; }
            public string 例外メッセージ { get; set; }
        }

        const string HowToUse
            = "Google TakeoutでダウンロードしたZIPファイルを展開し、展開されたファイル／フォルダをドラッグ＆ドロップして下さい。\n"
            + "コマンドライン引数やプログラムアイコンへのドラッグ＆ドロップでもファイル／フォルダ指定可能です。\n"
            + "画像ファイルにJSONファイル（例：IMG_1234.PNGに対しIMG_1234.PNG.json）がある場合に処理を行います。\n"
            + "TakeoutでZIPファイルが分割された場合には一つのフォルダに統合してください、画像ファイルとJSONファイルが同一のZIPに格納されるとは限りません。\n"
            + "JSONファイルに記録されているphotoTakenTime.timestampをLocal時間に変換し、画像ファイルとJSONファイルの作成日時と更新日時に設定します。";

        public MainWindow()
        {
            InitializeComponent();
            var resultList = new ObservableCollection<Result>();
            resultDataGrid.ItemsSource = resultList;
        }

        // 引数で指定されたときは即実行
        private void Window_ContentRendered(object sender, EventArgs e)
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
            {
                Thread thread = new Thread(new ParameterizedThreadStart(WorkerThread));
                thread.Start(args.Skip(1).ToArray());
            }
            else
            {
                ddPanel.AllowDrop = true;
                status.Content = HowToUse;
            }
        }

        // Drag＆Drop関連処理
        private void ddPanel_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop, true))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = e.Data.GetDataPresent(DataFormats.FileDrop);
            }
        }
        private void ddPanel_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                Thread thread = new Thread(new ParameterizedThreadStart(WorkerThread));
                thread.Start(e.Data.GetData(DataFormats.FileDrop));
            }
        }


        // UIが固まらないようにThreadで処理
        void WorkerThread(object arg)
        {
            Dispatcher.Invoke((Action)(() =>
            {
                ddPanel.AllowDrop = false;
                status.Content = "処理開始";
                var dataList = resultDataGrid.ItemsSource as ObservableCollection<Result>;
                dataList.Clear();
            }));
            DoEntries((string[])arg);
            Dispatcher.Invoke((Action)(() =>
            {
                ddPanel.AllowDrop = true;
                status.Content = "処理が終了しました。\n" + HowToUse;
            }));
        }
        void DoEntries(string[] args)
        {
            foreach (string arg in args)
            {
                if (File.Exists(arg) == true)
                {
                    DoFile(arg);
                }
                else
                {
                    try
                    {
                        foreach (var path in Directory.GetFiles(arg, "*", System.IO.SearchOption.AllDirectories))
                        {
                            DoFile(path);
                        }
                    }
                    catch (Exception e)
                    {
                        Dispatcher.Invoke((Action)(() =>
                        {
                            var dataList = resultDataGrid.ItemsSource as ObservableCollection<Result>;
                            dataList.Add(new Result() { 番号=dataList.Count+1, 結果=E_RESULT.失敗, ファイル名 = arg, エラーメッセージ="ファイルまたはフォルダが見つかりませんでした。",例外メッセージ = e.ToString() });
                        }));
                    }
                }
            }
        }

        void DoFile(string targetFilename)
        {
            var jsonFilename = targetFilename + ".json";
            if (string.Compare(".json", System.IO.Path.GetExtension(targetFilename), true) == 0)
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    status.Content = targetFilename;
                }));
            }
            else if (System.IO.File.Exists(jsonFilename))
            {
                DateTime photoTakenTime;
                try
                {
                    using (var stream = new FileStream(jsonFilename, FileMode.Open, FileAccess.Read))
                    {
                        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ImageInfo));
                        var imageInfo = (ImageInfo)serializer.ReadObject(stream);
                        var unixtime = imageInfo.PhotoTakenTime.timstamp;
                        photoTakenTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(unixtime)).LocalDateTime;
                    }
                    var imageFile = new System.IO.FileInfo(targetFilename);
                    var jsonFile = new System.IO.FileInfo(jsonFilename);

                    var results = new Result[] {
                        new Result(){ 結果=E_RESULT.SKIP,ファイル名=targetFilename,エラーメッセージ="",例外メッセージ="" },
                        new Result(){ 結果=E_RESULT.SKIP,ファイル名=jsonFilename,エラーメッセージ="画像ファイルの更新に失敗したときはJSONファイルも更新しません。",例外メッセージ="" }
                    };

                    foreach (var result in results)
                    {
                        try
                        {
                            var file = new System.IO.FileInfo(result.ファイル名);
                            file.CreationTime = photoTakenTime;
                            file.LastWriteTime = photoTakenTime;
                            result.結果 = E_RESULT.更新;
                            result.エラーメッセージ = "";
                            result.例外メッセージ = "";
                        }
                        catch (Exception e)
                        {
                            result.結果 = E_RESULT.失敗;
                            result.エラーメッセージ = "タイムスタンプの更新に失敗しました。";
                            result.例外メッセージ = e.ToString();
                            break;
                        }
                    }
                    Dispatcher.Invoke((Action)(() =>
                    {
                        status.Content = targetFilename;
                        var dataList = resultDataGrid.ItemsSource as ObservableCollection<Result>;
                        foreach (var result in results)
                        {
                            result.番号 = dataList.Count + 1;
                            dataList.Add(result);
                        }
                    }));
                }
                catch(Exception e)
                {
                    Dispatcher.Invoke((Action)(() =>
                    {
                        status.Content = targetFilename;
                        var dataList = resultDataGrid.ItemsSource as ObservableCollection<Result>;
                        dataList.Add(new Result() { 番号 = dataList.Count + 1, 結果 = E_RESULT.失敗, ファイル名 = jsonFilename, エラーメッセージ = "タイムスタンプの取得に失敗しました。", 例外メッセージ = e.ToString() });
                    }));
                }
            }
            else
            {
                Dispatcher.Invoke((Action)(() =>
                {
                    status.Content = targetFilename;
                    var dataList = resultDataGrid.ItemsSource as ObservableCollection<Result>;
                    dataList.Add(new Result() { 番号 = dataList.Count + 1, 結果 = E_RESULT.SKIP, ファイル名 = targetFilename, エラーメッセージ = "*.JSONファイルがありません。", 例外メッセージ = "" });
                }));
            }
        }
    }
}