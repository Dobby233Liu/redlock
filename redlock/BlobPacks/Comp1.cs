namespace redlock.BlobPacks;

internal class Comp1() : CompBlobPack<ShsxsBlob>(Data.Comp1)
{
	protected override ShsxsBlob[] Blobs { get; } =
	[
		new(2140160, [
			new BlobPatch(20015, 114),
			new BlobPatch(20040, 48)
		]),
		new(2139648, [
			new BlobPatch(16095, 114),
			new BlobPatch(16146, 48)
		])
	];

	public ShsxsBlob ShsxsAmd64 => Blobs[0];
	
	public ShsxsBlob ShsxsI386 => Blobs[1];
}