# Support the complete EF Core 10 patch line

Pomelo 10 will declare compatibility with EF Core `10.0.x` through the package range `[10.0.0,10.0.999]` and compile against the `10.0.0` floor so consumers are not forced onto a later patch. Validation will also include an explicitly pinned lane for the latest stable EF Core 10 patch, currently `10.0.11`, and that pin will advance as new patches are released.
