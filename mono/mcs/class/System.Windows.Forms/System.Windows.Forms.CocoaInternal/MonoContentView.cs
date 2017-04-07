//
//MonoView.cs
// 
//Author:
//	Lee Andrus <landrus2@by-rite.net>
//
//Copyright (c) 2009-2010 Lee Andrus
//
//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:
//
//The above copyright notice and this permission notice shall be included in
//all copies or substantial portions of the Software.
//
//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//THE SOFTWARE.
//

using System;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

#if XAMARINMAC
using AppKit;
#elif MONOMAC
using MonoMac.AppKit;
#endif

#if SDCOMPAT
using NSRect = System.Drawing.RectangleF;
#else
#if XAMARINMAC
using NSRect = CoreGraphics.CGRect;
#elif MONOMAC
using NSRect = MonoMac.CoreGraphics.CGRect;
#endif
#endif

namespace System.Windows.Forms.CocoaInternal
{
	partial class MonoContentView : MonoView
	{
		public MonoContentView (IntPtr instance) : base (instance)
		{
		}

		public MonoContentView (XplatUICocoa driver, NSRect frameRect, WindowStyles style, WindowExStyles exStyle)
			: base(driver, frameRect, style, exStyle)
		{
		}

		public override bool IsOpaque {
			get {
				return Window == null || Window.IsOpaque;
			}
		}

		#region Mouse

		public override void MouseEntered(NSEvent e)
		{
			Window.AcceptsMouseMovedEvents = true;
			base.MouseEntered(e);
		}

		public override void MouseExited(NSEvent e)
		{
			Window.AcceptsMouseMovedEvents = false;
			base.MouseExited(e);
		}

		#endregion
	}
}
