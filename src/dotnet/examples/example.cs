using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NAudio.Wave;
using static Whisper;

public class Program
{
  private static readonly WaveFormat Format = new(WHISPER_SAMPLE_RATE, 16, 1);
  private static readonly int ChunkSizeInSeconds = WHISPER_CHUNK_SIZE;
  private static readonly int ChunkSizeInBytes = Format.AverageBytesPerSecond * ChunkSizeInSeconds;
  private static readonly int ChunkSizeInFrames = ChunkSizeInBytes / Format.BlockAlign;

  private static float? GetNextFrame(WaveStream stream)
  {
    if (stream.WaveFormat.BitsPerSample != Format.BitsPerSample) throw new ArgumentException($"WaveStream format must be Program.Format. (BitsPerSample do not match.)", nameof(stream));
    if (stream.WaveFormat.SampleRate != Format.SampleRate) throw new ArgumentException($"WaveStream format must be Program.Format. (SampleRate do not match.)", nameof(stream));
    if (stream.WaveFormat.Channels != Format.Channels) throw new ArgumentException($"WaveStream format must be Program.Format. (Channels do not match.)", nameof(stream));


    var bytes = new byte[stream.WaveFormat.BlockAlign];

    var read = stream.Read(bytes, 0, bytes.Length);
    if (read == 0) return null;
    if (read < bytes.Length) throw new InvalidDataException("Unexpected end of file");
    return BitConverter.ToInt16(bytes, 0) / 32768f;
  }

  private static long GetLengthInFrames(WaveStream stream) => stream.Length / stream.BlockAlign;

  private static WaveStream Convert(WaveStream sourceStream)
  {
    return new WaveFormatConversionStream(Format, sourceStream);
  }

  public static IEnumerable<(int Index, Memory<float> Buffer)> ProcessFramesWithBuffer(Memory<float> buffer, WaveStream stream)
  {
    var i = -1;
    while (stream.Position < stream.Length)
    {
      i++;

      for (int f = 0; f < buffer.Length; f++)
      {
        var frame = GetNextFrame(stream);
        if (!frame.HasValue)
        {
          yield return (i, buffer[..(f - 1)]);
          yield break;
        }

        buffer.Span[f] = frame.Value;

      }

      yield return (i, buffer);
    }
  }

  private static void CompareProcessMethods(string path)
  {
    using var mp3s1 = new Mp3FileReader(path);
    using var wavs1 = Convert(mp3s1);
    var buffer1 = new Memory<float>(new float[GetLengthInFrames(wavs1)]);
    var result1 = ProcessFramesWithBuffer(buffer1, wavs1).Single().Buffer.Span;

    using var mp3s2 = new Mp3FileReader(path);
    using var wavs2 = Convert(mp3s2);
    var buffer2 = new Memory<float>(new float[ChunkSizeInFrames]);
    var result2 = ProcessFramesWithBuffer(buffer2, wavs2);

    var i = 0;

    foreach (var chunk in result2)
    {
      foreach (var frame2 in chunk.Buffer.Span)
      {
        Debug.Assert(result1[i] == frame2);
        i++;
      }
    }
 
  }


  private static void Process(SWIGTYPE_p_whisper_context context, whisper_full_params parameters, string inputPath, string outputPath)
  {

    // using (var mp3s = new Mp3FileReader(inputPath))
    // using (var wavs = Convert(mp3s))
    using (var wavs = new WaveFileReader(inputPath))
    using (var writer = new StreamWriter(outputPath))
    // using (var wavreader = new WaveFileReader(filePath))
    {
      if (wavs.WaveFormat.SampleRate != WHISPER_SAMPLE_RATE)
        throw new InvalidOperationException("Invalid sample rate");

      writer.WriteLine("Transcript of " + inputPath);
      writer.WriteLine();


      Console.WriteLine($"Processing {wavs.TotalTime} in length");

      // var buffer = new Memory<float>(new float[ChunkSizeInFrames]);
      var buffer = new Memory<float>(new float[GetLengthInFrames(wavs)]);

      foreach (var (chunki, chunk) in ProcessFramesWithBuffer(buffer, wavs))
      {
        Console.WriteLine($"Processing chunk {chunki}");

        if(chunki == 0) parameters.no_context = true;
        else parameters.no_context = false;

        var test = chunk.ToArray();

        // TODO: Update the wrapper to accept a span
        // https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines#usage-guidelines
        var ret = whisper_full(context, parameters, test, test.Length);
        if (ret != 0) throw new InvalidOperationException("Failed to process audio");

        var segmentsCount = whisper_full_n_segments(context);

        for (int i = 0; i < segmentsCount; i++)
        {
          var text = whisper_full_get_segment_text(context, i);
          writer.Write(text);
        }
      }

      whisper_print_timings(context);

    }
  }


  public static void Main()
  {
    // var files =
    //   Directory.EnumerateFiles("../../samples", "*.mp3", SearchOption.AllDirectories)
    //   .Order();

    var files = new[] {
      "C:\\Users\\nickd\\source\\repos\\whisper\\samples\\jfk1.wav",
    };


    var context = whisper_init("C:\\Users\\nickd\\source\\repos\\whisper\\samples\\ggml-base.en.bin");
    var parameters = whisper_full_default_params(whisper_sampling_strategy.WHISPER_SAMPLING_GREEDY);

    parameters.print_realtime = true;
    parameters.print_progress = false;
    parameters.print_timestamps = true;
    parameters.translate = false;
    parameters.n_threads = 4;
    parameters.offset_ms = 0;
    //parameters.duration_ms = 10_000;

    foreach (var inputPath in files)
    {
      var outputPath = Path.ChangeExtension(inputPath, "txt");
      
      using var input = File.OpenRead(inputPath);


      Console.WriteLine($"Processing {inputPath}");
      if (input.Length == 0)
      {
        Console.WriteLine($"Skipping empty file {inputPath}");
        continue;
      }
      // if (File.Exists(outputPath))
      // {
      //   Console.WriteLine($"Skipping existing transcription {inputPath}");
      //   continue;
      // }
      //CompareProcessMethods(file);

      Process(context, parameters, inputPath, outputPath);

      Console.WriteLine($"Processed {inputPath}");
      Console.WriteLine($"========================");
    }

    Console.WriteLine(whisper_print_system_info());

    whisper_free(context);

  }
}

// TODO: print the transcription timespans
// TODO: print the chunk timespans