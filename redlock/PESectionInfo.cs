namespace redlock
{
	internal class PESectionInfo
	{
		public string SectionName { get; set; }

		public int VirtSize { get; set; }

		public int VirtAddr { get; set; }

		public int PhysSize { get; set; }

		public int PhysAddr { get; set; }

		public int VirtOffset { get; set; }
	}
}