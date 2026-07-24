# Transpose compiler benchmark — r2r-debug

- **CPU**: Intel(R) Xeon(R) Processor @ 2.80GHz — 4 physical / 4 logical cores
- **RAM**: 15.7 GB
- **OS / runtime**: Ubuntu 24.04.4 LTS · .NET 10.0.10 (X64)
- **CPU features**: `Vector<T>=256bit Vector128.HwAccel Vector256.HwAccel SSE2 SSE3 SSSE3 SSE4.1 SSE4.2 AVX AVX2 AVX512F AVX512BW FMA BMI1 BMI2 LZCNT POPCNT AES PCLMULQDQ`
- **CPU score**: **95.2** (100 = reference machine; normalised times are `measured × score/100`)
- **Configuration**: Debug, 3 measured iteration(s) per scenario

| scenario | wall (median) | normalised | ±stddev | cpu-time | alloc | peak WS |
|---|--:|--:|--:|--:|--:|--:|
| `tesserae` | 6.00 s | 5.71 s | 0.09 s | 18.34 s | 604.0 MB | 406.4 MB |
| `tesserae+tests` | 7.15 s | 6.80 s | 0.08 s | 22.01 s | 828.5 MB | 530.6 MB |
| `tesserae+tests-warm` | 3.38 s | 3.21 s | 0.18 s | 9.39 s | 238.2 MB | 284.0 MB |

## `tesserae` phase breakdown

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project (csproj + globs + references) | 37 | 0.6% | 14.5 MB |
| build compilation (parse + references) | 287 | 4.9% | 28.9 MB |
| scan unsupported features | 1,264 | 21.6% | 56.6 MB |
| bind + emit .NET assembly | 1,139 | 19.5% | 57.2 MB |
|   &boxvr; collect + order types | 44 |  | 0.0 MB |
|   &boxvr; emit type bodies (parallel) | 1,487 |  | 0.0 MB |
|   &boxvr; concatenate type bodies | 4 |  | 0.0 MB |
|   &boxur; reflection metadata (file) | 373 |  | 0.0 MB |
| emit JavaScript | 1,898 | 32.5% | 279.0 MB |
| body diagnostics (semantic models) | 899 | 15.4% | 75.1 MB |
| collect package resources (minify + read files) | 60 | 1.0% | 39.9 MB |
| embed resources into DLL (Cecil) | 257 | 4.4% | 51.6 MB |

## `tesserae+tests` phase breakdown

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project (csproj + globs + references) | 22 | 0.3% | 3.9 MB |
| build compilation (parse + references) | 359 | 5.2% | 34.0 MB |
| scan unsupported features | 1,438 | 20.9% | 98.1 MB |
| bind + emit .NET assembly | 1,185 | 17.2% | 61.4 MB |
|   &boxvr; collect + order types | 61 |  | 0.0 MB |
|   &boxvr; emit type bodies (parallel) | 1,814 |  | 0.0 MB |
|   &boxvr; concatenate type bodies | 4 |  | 0.0 MB |
|   &boxur; reflection metadata (file) | 328 |  | 0.0 MB |
| emit JavaScript | 2,242 | 32.5% | 355.6 MB |
| body diagnostics (semantic models) | 1,150 | 16.7% | 114.8 MB |
| collect package resources (minify + read files) | 47 | 0.7% | 40.7 MB |
| embed resources into DLL (Cecil) | 323 | 4.7% | 55.1 MB |
|   &boxvr; reflection metadata (inline) | 16 |  | 0.0 MB |
| write site (minify + resources + html) | 128 | 1.9% | 48.4 MB |

## `tesserae+tests-warm` phase breakdown

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project (csproj + globs + references) | 21 | 0.6% | 3.9 MB |
| build compilation (parse + references) | 189 | 5.7% | 10.9 MB |
| scan unsupported features | 952 | 28.8% | 44.0 MB |
| bind + emit .NET assembly | 365 | 11.0% | 4.6 MB |
|   &boxvr; collect + order types | 38 |  | 0.0 MB |
|   &boxvr; emit type bodies (parallel) | 814 |  | 0.0 MB |
|   &boxvr; concatenate type bodies | 1 |  | 0.0 MB |
|   &boxvr; reflection metadata (inline) | 26 |  | 0.0 MB |
| emit JavaScript | 887 | 26.8% | 81.0 MB |
| body diagnostics (semantic models) | 534 | 16.2% | 39.7 MB |
| collect package resources (minify + read files) | 98 | 3.0% | 0.8 MB |
| embed resources into DLL (Cecil) | 119 | 3.6% | 3.6 MB |
| write site (minify + resources + html) | 141 | 4.3% | 48.5 MB |

```
Comparison vs baseline 'baseline' (recorded on Intel(R) Xeon(R) Processor @ 2.80GHz, score 92.5)
  scenario                   baseline      current        delta    alloc delta
  ---------------------- ------------ ------------ ------------ --------------
  tesserae                   15.82 s       5.71 s       -63.9%         -44.4%
  tesserae+tests             23.16 s       6.80 s       -70.6%         -48.9%
  tesserae+tests-warm         9.03 s       3.21 s       -64.4%         -55.0%
  (negative = faster / less allocation than the baseline)
```
