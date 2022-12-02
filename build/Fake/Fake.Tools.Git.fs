namespace Fake.Tools.Git

open Fake.Tools.Git

module FileStatus =

  /// <summary>
  /// Gets the changed files that have been staged.
  /// </summary>
  ///
  /// <param name="repositoryDir">The git repository.</param>
  /// <param name="revision">The revision to use.</param>
  let getStagedFiles repositoryDir =
    let _, msg, _ =
      CommandHelper.runGitCommand repositoryDir "diff --cached --name-status -z"

    msg
    |> Seq.map (fun line ->
      let a = line.Split ('\t')
      FileStatus.FileStatus.Parse a[0], a[1])
