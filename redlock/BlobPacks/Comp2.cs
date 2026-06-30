using redlock.Properties;

namespace redlock.BlobPacks;

internal class Comp2() : CompBlobPack(Resources.comp2)
{
	protected override Blob[] Blobs { get; } =
	[
		new(1982),
		new(595),
		new(944)
	];
	
	public Blob SysResetRedPill => Blobs[0];
	
	public Blob RedpillLog => Blobs[1];
	
	public Blob RedpillCerts => Blobs[2];
}