public static class DeskUiState
{
	public static bool IsAnyDeskUiOpen => ComputerTerminalSystem.IsAnyTerminalOpen || PhoneBookSystem.IsAnyPhoneBookOpen;
}
