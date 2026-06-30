namespace redlock.Blobs;

internal class ShsxsBlob : Blob
{
	public BlobPatch[] AltInitLauncherDataLayerPatches { get; }
	
	internal ShsxsBlob(int size, BlobPatch[] altInitLauncherDataLayerPatches)
		: base(size)
	{
		AltInitLauncherDataLayerPatches = altInitLauncherDataLayerPatches;
	}
}