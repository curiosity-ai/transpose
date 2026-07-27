# Transpose.Placeholders

This library provides placeholder attributes for Transpose shared projects. It allows you to use Transpose-specific attributes (like `[ObjectLiteral]`, `[Module]`, `[Name]`, etc.) in your shared code (e.g., DTOs, ViewModels) without taking a dependency on the full Transpose framework.

These attributes are "empty" implementations that have no runtime effect in your backend .NET code but are recognized by the Transpose compiler when the shared project is consumed by a Transpose frontend project.

## Usage

Add a reference to `Transpose.Placeholders` in your shared project:

```xml
<PackageReference Include="Transpose.Placeholders" Version="1.0.0" />
```

Then, you can use Transpose attributes as usual:

```csharp
using Transpose;

[ObjectLiteral]
public class MyDto
{
    [Name("fullName")]
    public string FullName { get; set; }
}
```
