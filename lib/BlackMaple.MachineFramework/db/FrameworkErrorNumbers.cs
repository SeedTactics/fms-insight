namespace BlackMaple.MachineFramework
{
	public static class FrameworkErrorNumbers
	{

		//framework gets 400-499

		public const int LockingTimeoutException = 400;
		public const int OverridePartInvalidNumberProcess = 401;
		public const int UnableToMovePallet = 402;
		public const int UnableToFindJob = 403;
		public const int UnableToUpdatePalletMaterial = 404;
		public const int CannotDeleteMaterialOnPallet = 405;

		public const int JobAlreadyExists = 406;
	}
}
