// Based on https://github.com/Mizux/cmake-swig/blob/068edcfff8b74eeebf7d556180c8e69ab9e056c7/dotnet/base.i
// 
// Copyright 2022 Mizux
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

%include "stdint.i"
%include "typemaps.i"

#if defined(SWIGCSHARP)
#if defined(SWIGWORDSIZE64)
// By default SWIG map C++ long int (i.e. int64_t) to C# int
// But we want to map it to C# long so we reuse the typemap for C++ long long.
// ref: https://github.com/swig/swig/blob/master/Lib/csharp/typemaps.i
// ref: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types
%define PRIMITIVE_TYPEMAP(NEW_TYPE, TYPE)
%clear NEW_TYPE;
%clear NEW_TYPE *;
%clear NEW_TYPE &;
%clear const NEW_TYPE &;
%apply TYPE { NEW_TYPE };
%apply TYPE * { NEW_TYPE * };
%apply TYPE & { NEW_TYPE & };
%apply const TYPE & { const NEW_TYPE & };
%enddef // PRIMITIVE_TYPEMAP
PRIMITIVE_TYPEMAP(long int, long long);
PRIMITIVE_TYPEMAP(unsigned long int, unsigned long long);
#undef PRIMITIVE_TYPEMAP
#endif // defined(SWIGWORDSIZE64)
#endif // defined(SWIGCSHARP)
