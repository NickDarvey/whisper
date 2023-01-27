%module WhisperRuntime

%include "arrays_csharp.i"
%include "base.i"

// Add necessary symbols to generated header
%{
#include <whisper.h>
%}

// 22.4.2 Managed arrays using P/Invoke default array marshalling
// https://www.swig.org/Doc4.0/SWIGDocumentation.html#CSharp_arrays
%apply float INPUT[]  {float *samples, float *data, float *lang_probs}
%apply int INPUT[]  {int *tokens, int *prompt_tokens}
%apply unsigned char INPUT[] {void *buffer}

// whisper_context is forward-declared.
// https://stackoverflow.com/a/10008434/1259408
%nodefaultctor whisper_context;
%nodefaultdtor whisper_context;
struct whisper_context { };

// TODO: Map `void *`
%ignore new_segment_callback_user_data;
%ignore encoder_begin_callback_user_data;

// TODO: Map callbacks
%ignore new_segment_callback;
%ignore encoder_begin_callback;

// TODO: Return a T array from a T *
// https://stackoverflow.com/a/57071144/1259408
%ignore whisper_get_probs;
%ignore prompt_tokens;
%ignore prompt_n_tokens;
%ignore whisper_get_logits;

// TODO: Figure out how you're supposed to use `whisper_model_loader`
// (which wants a `void * context`)
%ignore whisper_init;
%ignore whisper_model_loader;

// Process symbols in header
%include "../../ext/whisper.cpp/whisper.h"
