namespace redlock.BlobPacks;

internal class Comp4() : CompBlobPack<Blob>(Data.Comp4)
{
	protected override Blob[] Blobs { get; } =
	[
		new(53352),
		new(36398)
	];

	public Blob SxsRes5231 => Blobs[0];

	public Blob SxsRes5232 => Blobs[1];
}