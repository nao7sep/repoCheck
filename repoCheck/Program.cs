using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace repoCheck
{
    internal class Program
    {
        // 単機能ソフトなのでクラス分けせずパッと

        private static string mDirectoryPathsFilePath = Path.Join (Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location), "DirectoryPaths.txt");

        private static List <string>
            mIncludedDirectoryPaths = new List <string> (),
            mExcludedDirectoryPaths = new List <string> (),
            mRepoDirectoryPaths = new List <string> ();

        private static void iWriteLine (string value, ConsoleColor backgroundColor, ConsoleColor foregroundColor)
        {
            Console.BackgroundColor = backgroundColor;
            Console.ForegroundColor = foregroundColor;

            Console.WriteLine (value);

            Console.ResetColor ();
        }

        // 黄色も赤も RGB 値的にシンプルな色にはなっていない
        // 自分のパソコンでは、「黄色＋黒」と「赤＋白」が同じくらい目立つ
        // 「赤＋黒」では黄色の方が目立つ

        private static void iWriteWarningLine (string value)
        {
            iWriteLine (value, ConsoleColor.Yellow, ConsoleColor.Black);
        }

        private static void iWriteErrorLine (string value)
        {
            iWriteLine (value, ConsoleColor.Red, ConsoleColor.White);
        }

        // .git が含まれている可能性の低いディレクトリー名を決め打ちで
        private static string [] mExcludedDirectoryNames = { ".svn", ".vs", "bin", "obj" };

        static void Main (string [] args)
        {
            try
            {
                if (File.Exists (mDirectoryPathsFilePath) == false)
                    File.WriteAllText (mDirectoryPathsFilePath, string.Empty, Encoding.UTF8);

                else
                {
                    foreach (string xLine in File.ReadAllLines (mDirectoryPathsFilePath, Encoding.UTF8))
                    {
                        int xIndex;

                        // 空白系文字だけの行、全体がコメントの行（先行空白は OK）、: を含まない行を単純に無視

                        if (string.IsNullOrWhiteSpace (xLine) || Regex.Match (xLine, @"^\s*//", RegexOptions.Compiled) != Match.Empty || (xIndex = xLine.IndexOf (':')) < 0)
                            continue;

                        string xKey = xLine.Substring (0, xIndex).Trim (),
                            xValue = xLine.Substring (xIndex + 1).Trim ();

                        // コピペでの設定なのでスペルミスで「見つからない」は稀
                        // ディレクトリー削除後も古い設定が残っているだけのことが多い
                        // エラーメッセージを表示するほどのことでない

                        if (Directory.Exists (xValue) == false)
                            continue;

                        if (xKey.Equals ("Include", StringComparison.OrdinalIgnoreCase))
                            mIncludedDirectoryPaths.Add (xValue);

                        else if (xKey.Equals ("Exclude", StringComparison.OrdinalIgnoreCase))
                            mExcludedDirectoryPaths.Add (xValue);

                        else if (xKey.Length == 0)
                            iWriteWarningLine ("キーがありません: " + xLine);

                        else iWriteWarningLine ("キーを認識できません: " + xKey);
                    }
                }

                if (mIncludedDirectoryPaths.Count == 0)
                {
                    // 「じゃあ、入れ方を教えろよ」と思うが、おそらく自分しか使わないので
                    iWriteWarningLine ("DirectoryPaths.txt に有効なパスが含まれていません。");
                }

                else
                {
                    void iScanDirectory (DirectoryInfo directory)
                    {
                        try
                        {
                            if (mExcludedDirectoryPaths.Contains (directory.FullName, StringComparer.OrdinalIgnoreCase) ||
                                    mExcludedDirectoryNames.Contains (directory.Name, StringComparer.OrdinalIgnoreCase))
                                return;

                            if (directory.Name.Equals (".git", StringComparison.OrdinalIgnoreCase))
                            {
                                // .git で一致しているのだから C:\ などはありえない
                                string xParentDirectoryPath = directory.Parent!.FullName;

                                if (mRepoDirectoryPaths.Contains (xParentDirectoryPath, StringComparer.OrdinalIgnoreCase) == false)
                                    mRepoDirectoryPaths.Add (xParentDirectoryPath);

                                // .git 内にさらにレポジトリーがある可能性は低い
                                return;
                            }

                            foreach (DirectoryInfo xSubdirectory in directory.GetDirectories ())
                                iScanDirectory (xSubdirectory);

                            // untracked や changed があるのは、これから対処すればいい正常な状態なので警告扱い
                            // 空のディレクトリーは、直ちに削除または Exclude により除外されるべきものなのでエラー扱い
                            // 分かりやすく色を区別したいのもある

                            if (directory.GetFileSystemInfos ().Length == 0)
                                iWriteErrorLine (directory.FullName + " => Empty");
                        }

                        catch
                        {
                            // たいていパーミッション関連のエラー
                            // 対処法がないので無視
                        }
                    }

                    // A/B/C において A と A/B をスキャンすると A/B と A/B/C を二度チェックすることになる
                    // 重複チェックによりリストの最終的な内容は一意になるので問題視しない

                    foreach (string xPath in mIncludedDirectoryPaths)
                        iScanDirectory (new DirectoryInfo (xPath));

                    if (mRepoDirectoryPaths.Count == 0)
                        iWriteWarningLine ("レポジトリーが見つかりません。");

                    else
                    {
                        mRepoDirectoryPaths.Sort ((x, y) => string.Compare (Path.GetFileName (x), Path.GetFileName (y), true));

                        foreach (string xPath in mRepoDirectoryPaths)
                        {
                            // git が入っているなら PATH が通っているのが普通
                            // 設定ファイルで絶対パスを指定できるようにすることも考えたが、
                            //     「設定ファイルをさわる時間があるなら PATH を通してくれ」と割り切る

                            // Git - git Documentation
                            // https://git-scm.com/docs/git

                            // Git - git-ls-files Documentation
                            // https://git-scm.com/docs/git-ls-files

                            // Git - git-diff Documentation
                            // https://git-scm.com/docs/git-diff

                            ProcessStartInfo xStartInfo = new ProcessStartInfo ("git");

                            xStartInfo.WorkingDirectory = xPath;
                            xStartInfo.RedirectStandardOutput = true;

                            // まず、untracked と changed の数が分かるようにして様子見
                            // よく使っている GitKraken でも、リストのところにはこれらだけ表示される
                            // ワークフロー的に左から右に流れてほしいので、左に untracked

                            // いずれも数を抽出
                            // 対処が必要な状態かどうか分かる最小限の情報のみ表示
                            // 出力のフォーマットが変われば動かなくなるが、仕様的に枯れている部分なのでフォーマットが変わる可能性は低い

                            int xUntrackedFileCount = 0;

                            xStartInfo.ArgumentList.Add ("ls-files");

                            // Show other (i.e. untracked) files in the output
                            xStartInfo.ArgumentList.Add ("--others");

                            // Add the standard Git exclusions: .git/info/exclude, .gitignore in each directory, and the user's global exclusion file
                            xStartInfo.ArgumentList.Add ("--exclude-standard");

                            // How do you git show untracked files that do not exist in .gitignore - Stack Overflow
                            // https://stackoverflow.com/questions/3538144/how-do-you-git-show-untracked-files-that-do-not-exist-in-gitignore

                            using (Process? xProcess = Process.Start (xStartInfo))
                            {
                                if (xProcess != null)
                                {
                                    xProcess.WaitForExit ();

                                    if (xProcess.StandardOutput.EndOfStream == false)
                                    {
                                        // 1行1ファイルパス
                                        // 今後もずっとそうか不明だが、今のところ動いている

                                        while (xProcess.StandardOutput.ReadLine () != null)
                                            xUntrackedFileCount ++;
                                    }
                                }
                            }

                            xStartInfo.ArgumentList.Clear ();

                            int xChangedFileCount = 0;

                            xStartInfo.ArgumentList.Add ("diff");

                            // HEAD がないと、現状とインデックス（ステージされたものが入るところ）が比較される
                            // 全てステージされていれば、「現状」と「全てステージされた状態」が一致し、changed がゼロになる

                            // HEAD があると、現状と HEAD（最後のコミット）が比較され、インデックスはスルーされる
                            // この場合、全てステージされていても一つもステージされていなくても changed が正しくカウントされる

                            xStartInfo.ArgumentList.Add ("HEAD");

                            // Output only the last line of the --stat format containing total number of modified files, as well as number of added and deleted lines
                            xStartInfo.ArgumentList.Add ("--shortstat");

                            using (Process? xProcess = Process.Start (xStartInfo))
                            {
                                if (xProcess != null)
                                {
                                    xProcess.WaitForExit ();

                                    if (xProcess.StandardOutput.EndOfStream == false)
                                    {
                                        // 今のところは動いている
                                        // ファイルが一つなら単数形になる
                                        Match xMatch = Regex.Match (xProcess.StandardOutput.ReadToEnd (), "([0-9]+) files? changed", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

                                        if (xMatch != Match.Empty)
                                            xChangedFileCount = int.Parse (xMatch.Result ("$1"), CultureInfo.InvariantCulture);
                                    }
                                }
                            }

                            StringBuilder xBuilder = new StringBuilder ();

                            if (xUntrackedFileCount > 0)
                                xBuilder.Append (xUntrackedFileCount.ToString (CultureInfo.InvariantCulture) + " untracked");

                            if (xChangedFileCount > 0)
                            {
                                if (xBuilder.Length > 0)
                                    xBuilder.Append (", ");

                                xBuilder.Append (xChangedFileCount.ToString (CultureInfo.InvariantCulture) + " changed");
                            }

                            if (xBuilder.Length > 0)
                                iWriteWarningLine ($"{xPath} => {xBuilder}");

                            else Console.WriteLine (xPath);
                        }
                    }
                }
            }

            catch (Exception xException)
            {
                iWriteErrorLine (xException.ToString ());
            }

            Console.Write ("なんかおしてケロ～ ");
            Console.ReadKey (true);
            Console.WriteLine ();
        }
    }
}
