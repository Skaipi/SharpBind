# SharpBind

<div class="sharpbind-wordmark">
  <img class="sharpbind-logo-light" src="images/SharpBind_full_logo.svg" alt="SharpBind — C# bindings for Python" width="2048" height="682">
  <img class="sharpbind-logo-dark" src="images/SharpBind_full_logo_dark.svg" alt="" width="2048" height="682" aria-hidden="true">
</div>

SharpBind turns annotated C# classes into Python packages containing a native
library, readable wrappers, documentation and type stubs.

Start with [Getting started](getting-started.md), then read
[Exporting APIs](exporting-apis.md) and [Using generated Python APIs](python-runtime.md).

The first release supports synchronous methods, ordinary C# facades,
copied values and CPU DLPack tensor sharing. Generated packages do require consumer 
to have dotnet runtime installed.
