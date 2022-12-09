
using static Whisper;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        var ctx = whisper_init("for-tests-ggml-base.en.bin");
        var parameters = whisper_full_default_params(whisper_sampling_strategy.WHISPER_SAMPLING_GREEDY);
        parameters.print_realtime = true;
        parameters.print_progress = false;
        parameters.print_timestamps = true;
        parameters.translate = false;
        parameters.n_threads = 4;
        parameters.offset_ms = 0;

        var n_samples = WHISPER_SAMPLE_RATE;
        var pcmf32 = new float[n_samples];
        var ret = whisper_full(ctx, parameters, pcmf32, n_samples);

        var n_segments = whisper_full_n_segments(ctx);

        for (int i = 0; i < n_segments; i++)
        {
            var text_cur = whisper_full_get_segment_text(ctx, i);
            System.Console.WriteLine(text_cur);
        }

        whisper_print_timings(ctx);
        whisper_free(ctx);
    }
}