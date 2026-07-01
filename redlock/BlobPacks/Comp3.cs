namespace redlock.BlobPacks;

internal class Comp3() : CompBlobPack<Blob>(Data.Comp3)
{
	protected override Blob[] Blobs { get; } =
	[
		new(246),
		new(550),
		new(260)
	];
	
	public Blob DuiRes7Patch => Blobs[0];
	
	public Blob DuiRes8 => Blobs[1];
	
	public Blob DuiRes9 => Blobs[2];
}