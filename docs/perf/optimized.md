# Transpose compiler benchmark — final

- **CPU**: Intel(R) Xeon(R) Processor @ 2.80GHz — 4 physical / 4 logical cores
- **RAM**: 15.7 GB
- **OS / runtime**: Ubuntu 24.04.4 LTS · .NET 10.0.10 (X64)
- **CPU features**: `Vector<T>=256bit Vector128.HwAccel Vector256.HwAccel SSE2 SSE3 SSSE3 SSE4.1 SSE4.2 AVX AVX2 AVX512F AVX512BW FMA BMI1 BMI2 LZCNT POPCNT AES PCLMULQDQ`
- **CPU score**: **89.6** (100 = reference machine; normalised times are `measured × score/100`)
- **Configuration**: Debug, 3 measured iteration(s) per scenario

| scenario | wall (median) | normalised | ±stddev | cpu-time | alloc | peak WS |
|---|--:|--:|--:|--:|--:|--:|
| `tesserae` | 8.07 s | 7.23 s | 0.10 s | 23.55 s | 686.1 MB | 490.8 MB |
| `tesserae+tests` | 9.64 s | 8.64 s | 0.79 s | 28.96 s | 949.0 MB | 578.5 MB |
| `tesserae+tests-warm` | 4.72 s | 4.23 s | 0.22 s | 12.66 s | 284.0 MB | 285.0 MB |

## `tesserae` phase breakdown

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project (csproj + globs + references) | 47 | 0.6% | 14.5 MB |
| build compilation (parse + references) | 396 | 5.0% | 28.9 MB |
| scan unsupported features | 1,823 | 23.1% | 57.4 MB |
| bind + emit .NET assembly | 3,258 | 41.3% | 198.9 MB |
|   &boxvr; collect + order types | 77 |  | 0.0 MB |
|   &boxvr; emit type bodies (parallel) | 1,161 |  | 0.0 MB |
|   &boxvr; concatenate type bodies | 7 |  | 0.0 MB |
|   &boxur; reflection metadata (file) | 401 |  | 0.0 MB |
| emit JavaScript | 1,689 | 21.4% | 273.8 MB |
| collect package resources (minify + read files) | 49 | 0.6% | 39.9 MB |
| embed resources into DLL (Cecil) | 633 | 8.0% | 70.7 MB |

## `tesserae+tests` phase breakdown

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project (csproj + globs + references) | 30 | 0.3% | 3.9 MB |
| build compilation (parse + references) | 482 | 5.1% | 34.1 MB |
| scan unsupported features | 1,906 | 20.3% | 101.0 MB |
| bind + emit .NET assembly | 4,085 | 43.5% | 270.1 MB |
|   &boxvr; collect + order types | 74 |  | 0.0 MB |
|   &boxvr; emit type bodies (parallel) | 1,526 |  | 0.0 MB |
|   &boxvr; concatenate type bodies | 8 |  | 0.0 MB |
|   &boxur; reflection metadata (file) | 372 |  | 0.0 MB |
| emit JavaScript | 1,972 | 21.0% | 350.0 MB |
| collect package resources (minify + read files) | 52 | 0.6% | 40.8 MB |
| embed resources into DLL (Cecil) | 634 | 6.8% | 86.4 MB |
|   &boxvr; reflection metadata (inline) | 16 |  | 0.0 MB |
| write site (minify + resources + html) | 223 | 2.4% | 48.2 MB |

## `tesserae+tests-warm` phase breakdown

| phase | ms | share | alloc |
|---|--:|--:|--:|
| resolve project (csproj + globs + references) | 28 | 0.6% | 3.9 MB |
| build compilation (parse + references) | 269 | 5.9% | 11.0 MB |
| scan unsupported features | 1,546 | 33.7% | 46.8 MB |
| bind + emit .NET assembly | 1,566 | 34.2% | 76.1 MB |
|   &boxvr; collect + order types | 31 |  | 0.0 MB |
|   &boxvr; emit type bodies (parallel) | 651 |  | 0.0 MB |
|   &boxvr; concatenate type bodies | 1 |  | 0.0 MB |
|   &boxvr; reflection metadata (inline) | 33 |  | 0.0 MB |
| emit JavaScript | 752 | 16.4% | 79.3 MB |
| collect package resources (minify + read files) | 4 | 0.1% | 0.8 MB |
| embed resources into DLL (Cecil) | 281 | 6.1% | 15.8 MB |
| write site (minify + resources + html) | 136 | 3.0% | 48.3 MB |

```
Comparison vs baseline 'baseline' (recorded on Intel(R) Xeon(R) Processor @ 2.80GHz, score 92.5)
  scenario                   baseline      current        delta    alloc delta
  ---------------------- ------------ ------------ ------------ --------------
  tesserae                   15.82 s       7.23 s       -54.3%         -36.9%
  tesserae+tests             23.16 s       8.64 s       -62.7%         -41.5%
  tesserae+tests-warm         9.03 s       4.23 s       -53.1%         -46.4%
  (negative = faster / less allocation than the baseline)
```
