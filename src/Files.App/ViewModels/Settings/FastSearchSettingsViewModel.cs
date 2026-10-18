// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Windows.Input;

namespace Files.App.ViewModels.Settings
{
	public sealed partial class FastSearchSettingsViewModel : ObservableObject
	{
		private readonly IIndexedSearchEngine indexedSearchEngine = Ioc.Default.GetRequiredService<IIndexedSearchEngine>();

		public ICommand TurnOnCommand { get; }
		public ICommand RemoveCommand { get; }

		private IndexingServiceStatus status = IndexingServiceStatus.Unknown;

		private bool isBusy = true;
		public bool IsBusy
		{
			get => isBusy;
			private set
			{
				if (SetProperty(ref isBusy, value))
					RaiseStateChanged();
			}
		}

		private string statusText = string.Empty;
		public string StatusText
		{
			get => statusText;
			private set => SetProperty(ref statusText, value);
		}

		private bool deleteIndexOnRemove;
		public bool DeleteIndexOnRemove
		{
			get => deleteIndexOnRemove;
			set => SetProperty(ref deleteIndexOnRemove, value);
		}

		public bool CanTurnOn
			=> !IsBusy && IndexingServiceManager.CanManageService && status is IndexingServiceStatus.NotInstalled or IndexingServiceStatus.NeedsUpdate;

		public bool CanRemove
			=> !IsBusy && IndexingServiceManager.CanManageService && status is IndexingServiceStatus.Running or IndexingServiceStatus.Stopped or IndexingServiceStatus.NeedsUpdate;

		// Once the service is set up, removing it is the only action, so the turn-on button goes away instead of greying out
		public bool IsTurnOnVisible
			=> status is not (IndexingServiceStatus.Running or IndexingServiceStatus.Stopped);

		public string TurnOnText
			=> status is IndexingServiceStatus.NeedsUpdate ? Strings.FastSearchUpdateService.GetLocalizedResource() : Strings.FastSearchTurnOn.GetLocalizedResource();

		public FastSearchSettingsViewModel()
		{
			TurnOnCommand = new AsyncRelayCommand(TurnOnAsync);
			RemoveCommand = new AsyncRelayCommand(RemoveAsync);

			_ = RefreshAsync();
		}

		private async Task TurnOnAsync()
		{
			IsBusy = true;
			var result = await IndexingServiceManager.InstallOrUpdateAsync(indexedSearchEngine);
			await RefreshAsync(result);
		}

		private async Task RemoveAsync()
		{
			IsBusy = true;
			var result = await IndexingServiceManager.UninstallAsync(indexedSearchEngine, DeleteIndexOnRemove);
			await RefreshAsync(result);
		}

		private async Task RefreshAsync(IndexingServiceActionResult? lastAction = null)
		{
			IsBusy = true;
			status = await Task.Run(IndexingServiceManager.GetStatus);

			StatusText = lastAction switch
			{
				IndexingServiceActionResult.Failed => Strings.FastSearchActionFailed.GetLocalizedResource(),
				_ when !IndexingServiceManager.CanManageService && status is IndexingServiceStatus.NotInstalled or IndexingServiceStatus.NeedsUpdate
					=> Strings.FastSearchStatusNotAvailable.GetLocalizedResource(),
				_ => status switch
				{
					IndexingServiceStatus.NotInstalled => Strings.FastSearchStatusOff.GetLocalizedResource(),
					IndexingServiceStatus.NeedsUpdate => Strings.FastSearchStatusNeedsUpdate.GetLocalizedResource(),
					IndexingServiceStatus.Running or IndexingServiceStatus.Stopped => Strings.FastSearchStatusOn.GetLocalizedResource(),
					_ => Strings.FastSearchStatusUnknown.GetLocalizedResource(),
				},
			};

			if (status is IndexingServiceStatus.Running or IndexingServiceStatus.Stopped)
				indexedSearchEngine.EnsureStarted();

			IsBusy = false;
			RaiseStateChanged();
		}

		private void RaiseStateChanged()
		{
			OnPropertyChanged(nameof(CanTurnOn));
			OnPropertyChanged(nameof(CanRemove));
			OnPropertyChanged(nameof(IsTurnOnVisible));
			OnPropertyChanged(nameof(TurnOnText));
		}
	}
}
