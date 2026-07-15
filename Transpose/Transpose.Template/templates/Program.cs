using System;
using Transpose;
using Transpose.Core;
using static Transpose.Core.es5;
using static Transpose.Core.dom;
using Tesserae;
using static Tesserae.UI;

namespace MyProject
{
    class Program
    {
        static void Main(string[] args)
        {
            var hello = TextBlock("Hello world!");

            document.body.appendChild(hello.Render());
        }
    }
}
