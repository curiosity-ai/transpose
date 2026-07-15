# H5.Placeholders

This library provides placeholder attributes for H5 shared projects. It allows you to use H5-specific attributes (like `[ObjectLiteral]`, `[Module]`, `[Name]`, etc.) in your shared code (e.g., DTOs, ViewModels) without taking a dependency on the full H5 framework.

These attributes are "empty" implementations that have no runtime effect in your backend .NET code but are recognized by the H5 compiler when the shared project is consumed by an H5 frontend project.

## Usage

Add a reference to `H5.Placeholders` in your shared project:

```xml
<PackageReference Include="H5.Placeholders" Version="1.0.0" />
```

Then, you can use H5 attributes as usual:

```csharp
using H5;

[ObjectLiteral]
public class MyDto
{
    [Name("fullName")]
    public string FullName { get; set; }
}
```
