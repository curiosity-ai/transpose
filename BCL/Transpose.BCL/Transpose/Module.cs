using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Transpose
{
    [External]
    [Name("Transpose")]
    public static class Module
    {
        [Template("Transpose.loadModule({type:module})")]
        public static extern Task Load(params Type[] type);
    }
}