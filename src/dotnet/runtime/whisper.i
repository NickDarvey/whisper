%module Whisper

%include "arrays_csharp.i"
%include "base.i"

// %template(StringVector) std::vector<std::string>;
// %template(StringJaggedArray) std::vector<std::vector<std::string>>;

// %template(IntPair) std::pair<int, int>;
// %template(PairVector) std::vector<std::pair<int, int>>;
// %template(PairJaggedArray) std::vector<std::vector<std::pair<int, int>>>;

// Add necessary symbols to generated header
%{
#include <whisper.h>
%}

// 22.4.2 Managed arrays using P/Invoke default array marshalling
// https://www.swig.org/Doc4.0/SWIGDocumentation.html#CSharp_arrays
// TODO: What about letting us pass a Span<float>?
%apply float INPUT[]  {float *samples}

// %ignore ""; // ignore all
// %define %unignore %rename("%s") %enddef

// %unignore foo;
// namespace foo {
// %rename("StringVectorOutput") stringVectorOutput(int);
// %rename("StringVectorInput") stringVectorInput(std::vector<std::string>);
// %rename("StringVectorRefInput") stringVectorRefInput(const std::vector<std::string>&);

// %unignore stringJaggedArrayOutput(int);
// %unignore stringJaggedArrayInput(std::vector<std::vector<std::string>>);
// %unignore stringJaggedArrayRefInput(const std::vector<std::vector<std::string>>&);

// %rename("PairVectorOutput") pairVectorOutput(int);
// %rename("PairVectorInput") pairVectorInput(std::vector<std::pair<int, int>>);
// %rename("PairVectorRefInput") pairVectorRefInput(const std::vector<std::pair<int, int>>&);

// %rename("PairJaggedArrayOutput") pairJaggedArrayOutput(int);
// %rename("PairJaggedArrayInput") pairJaggedArrayInput(std::vector<std::vector<std::pair<int, int>>>);
// %rename("PairJaggedArrayRefInput") pairJaggedArrayRefInput(const std::vector<std::vector<std::pair<int, int>>>&);

// %rename("FreeFunction") freeFunction(int);
// %rename("FreeFunction") freeFunction(int64_t);

// %unignore Whisper;
// %rename("StaticFunction") Whisper::staticFunction(int);
// %rename("StaticFunction") Whisper::staticFunction(int64_t);

// %rename("GetInt") Whisper::getInt() const;
// %rename("SetInt") Whisper::setInt(int);

// %rename("GetInt64") Whisper::getInt64() const;
// %rename("SetInt64") Whisper::setInt64(int64_t);

// %rename ("ToString") Whisper::operator();
// } // namespace foo

// Process symbols in header
%include "../../ext/whisper.cpp/whisper.h"

// %unignore ""; // unignore all