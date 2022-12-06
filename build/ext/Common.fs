/// Extensions to FSharp.Core
[<AutoOpen>]
module Common

module List =
  /// <summary>
  /// Creates two lists by applying the mapping function to each element in the list
  /// and classifying the transformed values depending on whether they were wrapped with Choice1Of2 or Choice2Of2.
  /// </summary>
  /// <returns>
  /// A tuple with both resulting lists.
  /// </returns>
  let partitionMap (mapping : 'T -> Choice<'T1, 'T2>) (source : list<'T>) =
    let rec loop ((acc1, acc2) as acc) =
      function
      | [] -> acc
      | x :: xs ->
        match mapping x with
        | Choice1Of2 x -> loop (x :: acc1, acc2) xs
        | Choice2Of2 x -> loop (acc1, x :: acc2) xs

    loop ([], []) (List.rev source)
