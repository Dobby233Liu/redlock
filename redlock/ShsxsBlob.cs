namespace redlock;

internal class ShsxsBlob : Blob
{
	internal ShsxsBlob(int size, BlobPatch[] altInitLauncherDataLayerPatches)
		: base(size)
	{
		AltInitLauncherDataLayerPatches = altInitLauncherDataLayerPatches;
	}

	public BlobPatch[] AltInitLauncherDataLayerPatches { get; }
}