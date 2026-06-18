using System;

namespace redlock;

[Flags]
internal enum UiFilePatchFlags
{
	None = 0,
	TouchEditInner = 1,
	ItemHeightInPopup = 2,
	TouchSelectPopup = 4,
	WrappingList = 8,
	TouchCarouselScrollBar = 16,
	TouchSwitch = 32,
	TouchEditDeprecated = 64
}