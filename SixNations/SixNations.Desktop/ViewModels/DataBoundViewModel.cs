﻿// Pre Standard .Net (see http://www.mvvmlight.net/std10) using CommonServiceLocator;
using GalaSoft.MvvmLight.Ioc;
using System;
using log4net;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using GalaSoft.MvvmLight;
using SixNations.Desktop.Helpers;
using SixNations.Desktop.Interfaces;
using SixNations.Data.Models;
using SixNations.Desktop.Messages;
using System.Windows.Input;
using GalaSoft.MvvmLight.CommandWpf;
using SixNations.API.Interfaces;

namespace SixNations.Desktop.ViewModels
{
    public abstract class DataBoundViewModel<T> : ViewModelBase, IAsyncViewModel
        where T : IHttpDataServiceModel, new()
    {
        private static readonly ILog Log = LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        protected readonly IDataService<T> DataService;
        private T _selectedItem;
        private bool _canSelectItem;

        public DataBoundViewModel(IDataService<T> dataService)
        {
            DataService = dataService;
            Index = new ObservableCollection<T>();

            NewCmd = new RelayCommand(OnNew, () => CanExecuteNew);
            EditCmd = new RelayCommand(OnEdit, () => CanExecuteSelectedItemChange);
            DeleteCmd = new RelayCommand(OnDelete, () => CanExecuteSelectedItemChange);
            SaveCmd = new RelayCommand(OnSave, () => CanExecuteSave);
            CancelCmd = new RelayCommand(OnCancel, () => CanExecuteCancel);
        }

        public async virtual Task LoadAsync()
        {
            MessengerInstance.Send(new BusyMessage(true, this));

            try
            {
                await LoadIndexAsync();

                CanSelectItem = true;
            }
            catch (Exception ex)
            {
                Log.ErrorFormat("Unexpexted exception loading! {0}", ex);
                FeedbackActions.ReactToException(ex);
            }
            finally
            {
                MessengerInstance.Send(new BusyMessage(false, this));
            }
        }

        protected virtual async Task LoadIndexAsync()
        {
            IEnumerable<T> data = null;
            if (User.Current.IsLoggedIn)
            {
                try
                {
                    data = await DataService.GetModelDataAsync(
                        User.Current.AuthToken, FeedbackActions.ReactToException);
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                    throw;
                }
            }
            if (data != null)
            {
                Index.Clear();
                foreach (var item in data)
                {
                    Index.Add(item);
                }
            }
            else
            {
                Index.Clear();
            }
        }

        public ObservableCollection<T> Index { get; }

        public bool IsSelectedItemEditable => SelectedItem != null &&
            SelectedItem.IsLockedForEditing;

        public bool CanSelectItem
        {
            get => _canSelectItem;
            set => Set(ref _canSelectItem, value);
        }

        public T SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (value == null)
                {
                    Set(ref _selectedItem, value);
                }
                else if (CanSelectItem)
                {
                    Set(ref _selectedItem, value);
                }
                RaisePropertyChanged(nameof(IsSelectedItemEditable));
            }
        }

        public ICommand NewCmd { get; }

        public ICommand EditCmd { get; }

        public ICommand DeleteCmd { get; }

        public ICommand SaveCmd { get; }

        public ICommand CancelCmd { get; }

        // TODO: Add permissions check on current user
        public bool CanExecute => !SimpleIoc.Default.GetInstance<MainViewModel>().BusyStateManager.IsBusy;

        // TODO: Add permissions check on current user
        public bool CanExecuteNew => CanExecute && CanSelectItem;

        // TODO: Add permissions check on current user
        public bool CanExecuteSelectedItemChange => CanExecute &&
            !IsSelectedItemEditable;

        public bool CanExecuteCancel => IsSelectedItemEditable;

        public bool CanExecuteSave => CanExecute && IsSelectedItemEditable &&
            SelectedItem.IsDirty;

        private async void OnNew()
        {
            MessengerInstance.Send(new BusyMessage(true, this));
            try
            {
                SelectedItem = await DataService.CreateModelAsync(
                    User.Current.AuthToken, FeedbackActions.ReactToException);
                RaisePropertyChanged(nameof(IsSelectedItemEditable));
                CanSelectItem = false;
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected Error Creating! {ex}");
                throw;
            }
            finally
            {
                MessengerInstance.Send(new BusyMessage(false, this));
            }
        }

        private async void OnEdit()
        {
            MessengerInstance.Send(new BusyMessage(true, this));
            try
            {
                SelectedItem = await DataService.EditModelAsync(
                    User.Current.AuthToken, FeedbackActions.ReactToException, SelectedItem);
                RaisePropertyChanged(nameof(IsSelectedItemEditable));
                CanSelectItem = false;
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected Error Saving! {ex}");
                throw;
            }
            finally
            {
                MessengerInstance.Send(new BusyMessage(false, this));
            }
        }

        private async void OnDelete()
        {
            var confirmed = ActionConfirmation.Confirm(ActionConfirmations.Delete);
            if (confirmed)
            {
                MessengerInstance.Send(new BusyMessage(true, this));
                bool result;
                try
                {
                    result = await DataService.DeleteModelAsync(
                        User.Current.AuthToken, FeedbackActions.ReactToException, SelectedItem);
                    RaisePropertyChanged(nameof(IsSelectedItemEditable));
                    if (result)
                    {
                        await LoadIndexAsync();

                        SelectedItem = Index.First();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Unexpected Error Deleting! {ex}");
                    throw;
                }
                finally
                {
                    MessengerInstance.Send(new BusyMessage(false, this));
                }
            }
        }

        private async void OnSave()
        {
            MessengerInstance.Send(new BusyMessage(true, this));
            try
            {
                T updated;
                if (SelectedItem.IsNew)
                {
                    updated = await DataService.StoreModelAsync(
                        User.Current.AuthToken, FeedbackActions.ReactToException, SelectedItem);
                }
                else
                {
                    updated = await DataService.UpdateModelAsync(
                        User.Current.AuthToken, FeedbackActions.ReactToException, SelectedItem);
                }
                RaisePropertyChanged(nameof(IsSelectedItemEditable));
                CanSelectItem = true;
                if (updated != null)
                {
                    await LoadIndexAsync();
                    SelectedItem = Index.FirstOrDefault(r => r.Id == updated.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Unexpected Error Saving! {ex}");
                throw;
            }
            finally
            {
                MessengerInstance.Send(new BusyMessage(false, this));
            }
        }

        private async void OnCancel()
        {
            var confirmed = !SelectedItem.IsDirty;
            if (!confirmed)
            {
                confirmed = ActionConfirmation.Confirm(ActionConfirmations.Cancel);
            }
            if (confirmed)
            {
                MessengerInstance.Send(new BusyMessage(true, this));
                await LoadIndexAsync();
                CanSelectItem = true;
                SelectedItem = Index.FirstOrDefault();
                MessengerInstance.Send(new BusyMessage(false, this));
            }
        }
    }
}
