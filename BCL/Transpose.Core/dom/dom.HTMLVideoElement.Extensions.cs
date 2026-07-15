using Transpose;
using Transpose.Core;
using System;

namespace Transpose.Core
{
    public static partial class dom
    {
        public partial class HTMLVideoElement
        {
            public virtual extern es5.Promise<dom.PictureInPictureWindow> requestPictureInPicture();

            public virtual bool disablePictureInPicture
            {
                get;
                set;
            }

            public virtual dom.HTMLVideoElement.onenterpictureinpictureFn onenterpictureinpicture
            {
                get;
                set;
            }

            public virtual dom.HTMLVideoElement.onleavepictureinpictureFn onleavepictureinpicture
            {
                get;
                set;
            }

            [Generated]
            public delegate void onenterpictureinpictureFn(dom.PictureInPictureEvent ev);

            [Generated]
            public delegate void onleavepictureinpictureFn(dom.PictureInPictureEvent ev);
        }
    }
}
