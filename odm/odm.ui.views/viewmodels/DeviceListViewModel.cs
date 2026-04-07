using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Unity;
using odm.core;
using odm.infra;
using odm.models;
using odm.ui.core;
using odm.ui.views;
using onvif.utils;
using utils;

namespace odm.ui.viewModels {
	public class DevicesObservableCollection : ObservableCollection<DeviceDescriptionHolder> {
		public void AddBindedCollection(ObservableCollection<DeviceDescriptionHolder> displayCollection) {
			this.displayCollection = displayCollection;
		}
		ObservableCollection<DeviceDescriptionHolder> displayCollection;
		public void Clear() {
			if (displayCollection != null) {
				displayCollection.Clear();
			}
			base.Clear();
		}
		void AddToDisplay(DeviceDescriptionHolder dholder) {
			FilterDisplayList();
		}
		void FilterDisplayList() {
			if (displayCollection == null)
				return;

			//add all missing devices
			this.ForEach(dev => {
				if (!displayCollection.Contains(dev))
					displayCollection.Add(dev);
			});
			//remove all remooved devices
			displayCollection.ForEach(dev => {
				if (!this.Contains(dev))
					displayCollection.Remove(dev);
			});

			var ordered = displayCollection.OrderBy(dev => dev.IsManual);

			int index = 0;
			ordered.ForEach(dev => {

				var currInd = displayCollection.IndexOf(dev);
				if (currInd != index) {
					displayCollection.Move(currInd, index);
				}

				index++;
			});
			if (Filter != "") {
				List<DeviceDescriptionHolder> devlist = new List<DeviceDescriptionHolder>();
				displayCollection.ForEach(dev => { 
					bool ret = false;
					if (dev.Name != null)
						ret = ret || dev.Name.ToUpper().Contains(Filter.ToUpper());
					else
						ret = true;
					if (dev.Location != null)
						ret = ret || dev.Location.ToUpper().Contains(Filter.ToUpper());
					else
						ret = true;
					if (dev.IsManual) {
						try {
							Uri uri = new Uri(dev.ManualDevice.DevUri);
							ret = ret || uri.Host.Contains(Filter);
						} catch (Exception err) {
							dbg.Error(err.Message);
							ret = true;
						}
					} else {
						if (dev.Uris != null) {
							dev.Uris.ForEach(duri => {
								ret = ret || duri.Host.Contains(Filter);
							});
						} else {
							ret = true;
						}
					}
					if (!ret)
						devlist.Add(dev);
				});
				devlist.ForEach(dev => {
					displayCollection.Remove(dev);
				});
			}
		}
		string filter = "";
		public string Filter { 
			get{return filter;}
			set {
				filter = value;
				FilterDisplayList();
			}
		}
		public void Remove(DeviceDescriptionHolder dholder) {
			if (displayCollection != null) {
				if(displayCollection.Contains(dholder))
					displayCollection.Remove(dholder);
			}
			base.Remove(dholder);
		}
		public void Add(DeviceDescriptionHolder dholder) {
			base.Add(dholder);
			AddToDisplay(dholder);
		}
	}
	public class DeviceListViewModel : DependencyObject, IDisposable {
		public LocalDeviceList Strings { get { return LocalDeviceList.instance; } }
		public LocalButtons BtnStrings { get { return LocalButtons.instance; } }
		public LocalTitles Titles { get { return LocalTitles.instance; } }

		private INvtManager deviceManager;
		private readonly IEventAggregator eventAggregator;
		private readonly IUnityContainer container;
		private CompositeDisposable subscriptions;
		private SerialDisposable discoverSubscription;
		System.Windows.Threading.Dispatcher currentDispatcher;
		SubscriptionToken RefreshSubscribtion;
        SubscriptionToken BatchUpgradeSubscribtion;
        SubscriptionToken BatchRestoreSubscribtion;
		// Per-device session factory tracking — each device may authenticate with a different credential
		private Dictionary<DeviceDescriptionHolder, NvtSessionFactory> _deviceFactories = new Dictionary<DeviceDescriptionHolder, NvtSessionFactory>();
		// Per-device credential cache — keyed on device host string, value is the last successful Account.
		// Consulted before full credential iteration to avoid re-trying all credentials on every refresh.
		// Evicted on auth failure so that password changes are handled after one extra failed attempt.
		private readonly Dictionary<string, Account> _credentialCache = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);


		public DeviceListViewModel(IUnityContainer container) {//IDeviceManager deviceManager, IEventAggregator eventAggregator) {
			currentDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

			Devices = new DevicesObservableCollection();
			DisplayDevices = new ObservableCollection<DeviceDescriptionHolder>();
			Devices.AddBindedCollection(DisplayDevices);
			
			subscriptions = new CompositeDisposable();
			discoverSubscription = new SerialDisposable();

			this.container = container;
			//this.deviceManager = container.Resolve<INvtManager>();
			this.eventAggregator = container.Resolve<EventAggregator>();

			RefreshSubscribtion = eventAggregator.GetEvent<Refresh>().Subscribe(Refresh, false);
            BatchUpgradeSubscribtion = eventAggregator.GetEvent<UpgradeButchClick>().Subscribe(PublishBatchUpgrade, false);
            BatchRestoreSubscribtion = eventAggregator.GetEvent<RestoreButchClick>().Subscribe(PublishBatchRestore, false);

			InitCommands();
			
			currentAccount = LoadCurrentAccount();
			_sessionFactory = new NvtSessionFactory(currentAccount);

			LoadManualList();

			//LoadDevices();
		}

#region manual
		void SaveManualList() {
			var mandev = Devices.Where(d => d.IsManual);
			List<ManualDevice> mdev= new List<ManualDevice>();
			mandev.ForEach(dev=>{
				mdev.Add(dev.ManualDevice);
			});
			ManualUriManager.Save(mdev);
		}
		void LoadManualList() {
			var mlst = ManualUriManager.Load();
			mlst.ForEach(item => {
				ManualLoaded(item.DevUri);
			});
		}

		void ManualLoaded(string struri) {
			if (!Uri.IsWellFormedUriString(struri, UriKind.Absolute))
				return;

			Uri uri;
			if (!Uri.TryCreate(struri, UriKind.Absolute, out uri))
				return;
			
			DeviceDescriptionHolder devHolder = new DeviceDescriptionHolder();
			
			//scopes
			//var scopes = node.identity.scopes.Select(x => x.OriginalString);
			
			devHolder.Uris = new Uri[] { uri };
			devHolder.Uris.ForEach(x => { devHolder.Address += x.DnsSafeHost + "; "; });
			devHolder.Address = devHolder.Address.TrimEnd(new Char[] { ';', ' ' });

			devHolder.Account = GetCurrentAccount();

			ManualSessionProcess(devHolder);

			devHolder.IsManual = true;
			devHolder.ManualDevice = new ManualDevice() { DevUri = struri };
			devHolder.FillCommands(ddell => {
				ManualDelete(ddell);
			}, dedit => {
				ManualEdit(dedit);
			});


			Devices.Add(devHolder);
			SaveManualList();
		}
		void ManualSessionProcess(DeviceDescriptionHolder devHolder) {
			var cacheKey = GetDeviceCacheKey(devHolder);
			Account cachedAccount;
			if (cacheKey != null && _credentialCache.TryGetValue(cacheKey, out cachedAccount)) {
				var cachedCred = cachedAccount.IsAnonymous
					? null
					: new System.Net.NetworkCredential() { UserName = cachedAccount.Name, Password = cachedAccount.Password };
				var cachedFactory = new NvtSessionFactory(cachedCred);
				IdentitySubscriptions.Add(cachedFactory.CreateSession(devHolder.Uris)
					.ObserveOnCurrentDispatcher()
					.Subscribe(session => {
						devHolder.Account = cachedCred;
						_deviceFactories[devHolder] = cachedFactory;
						ManualInitDeviceHolder(session, devHolder);
					}, err => {
						_credentialCache.Remove(cacheKey);
						FullCredentialIterationManual(devHolder);
					}));
				return;
			}

			FullCredentialIterationManual(devHolder);
		}

		void FullCredentialIterationManual(DeviceDescriptionHolder devHolder) {
			var allCreds = AccountManager.Instance.GetAllCredentials();
			var attempts = new List<System.Net.NetworkCredential>();

			foreach (var cred in allCreds) {
				if (!cred.IsAnonymous) {
					attempts.Add(new System.Net.NetworkCredential() { UserName = cred.Name, Password = cred.Password });
				}
			}
			attempts.Add(null); // anonymous fallback

			TryNextCredentialManual(devHolder, attempts, 0);
		}

		void TryNextCredentialManual(DeviceDescriptionHolder devHolder, List<System.Net.NetworkCredential> credentials, int index) {
			if (index >= credentials.Count) {
				// All credentials exhausted — anonymous last resort
				var fallbackFactory = new NvtSessionFactory(null);
				_deviceFactories[devHolder] = fallbackFactory;
				devHolder.Account = null;
				CacheCredential(devHolder, Account.Anonymous);
				IdentitySubscriptions.Add(fallbackFactory.CreateSession(devHolder.Uris)
					.ObserveOnCurrentDispatcher()
					.Subscribe(session => {
						ManualInitDeviceHolder(session, devHolder);
					}, err => {
						dbg.Error(err);
					}));
				return;
			}

			var cred = credentials[index];
			var factory = new NvtSessionFactory(cred);
			IdentitySubscriptions.Add(factory.CreateSession(devHolder.Uris)
				.ObserveOnCurrentDispatcher()
				.Subscribe(session => {
					devHolder.Account = cred;
					_deviceFactories[devHolder] = factory;
					CacheCredential(devHolder, new Account() { Name = cred.UserName, Password = cred.Password });
					ManualInitDeviceHolder(session, devHolder);
				}, err => {
					TryNextCredentialManual(devHolder, credentials, index + 1);
				}));
		}

		void ManualInitDeviceHolder(INvtSession session, DeviceDescriptionHolder devHolder) {
			devHolder.session = session;

			facade = new OdmSession(session);
			var model = new IdentificationModel();
			IdentitySubscriptions.Add(
					  facade.GetIdentity(() => model)
							.ObserveOnCurrentDispatcher()
							.Subscribe(mod => {
								devHolder.Init(mod);
							}, err => {
								//dbg.Error(err);
								//MessageBox.Show(err.Message);
							})
				 );
		}
		void ManualAdd() {
			ManualUri manUri = new ManualUri(LocalTitles.instance.manualAdd, ManualUri.ManualType.ADD, "");
			manUri.Owner = Application.Current.MainWindow;
			if (manUri.ShowDialog().Value == true) {
				var ret = manUri.devUri;
				ManualLoaded(ret);
			}
		}
		void ManualEdit(DeviceDescriptionHolder dhold) {
			ManualUri manUri = new ManualUri(LocalTitles.instance.manualEdit, ManualUri.ManualType.EDIT, dhold.ManualDevice.DevUri);
			manUri.Owner = Application.Current.MainWindow;

			if (manUri.ShowDialog().Value == true) {
				var ret = manUri.devUri;
				Devices.Remove(dhold);
				ManualLoaded(ret);
			}
		}
		void ManualDelete(DeviceDescriptionHolder dhold) {
			Devices.Remove(dhold);

			SaveManualList();
		}

#endregion manual

		void ReleaseDeviceSubscription() {
			IdentitySubscriptions.Dispose();
			IdentitySubscriptions = new CompositeDisposable();
			subscriptions.Dispose();
			subscriptions = new CompositeDisposable();
			if (discoverSubscription != null)
				discoverSubscription.Dispose();
			discoverSubscription = new SerialDisposable();
		}

        void PublishBatchUpgrade(bool res) {
            eventAggregator.GetEvent<UpgradeButchReady>().Publish(GetBatchTaskEventArgs());
        }
        void PublishBatchRestore(bool res) {
            eventAggregator.GetEvent<RestoreButchReady>().Publish(GetBatchTaskEventArgs());
        }
        BatchTaskEventArgs GetBatchTaskEventArgs() {
            BatchTaskEventArgs evargs = new BatchTaskEventArgs();
            evargs.currentAccount = AccountManager.Instance.CurrentAccount;
            evargs.Devices = DisplayDevices.ToList();
            return evargs;
        }

		void PublishRefresh() {
			eventAggregator.GetEvent<Refresh>().Publish(false);
		}
		void Refresh(bool res) {
			if (res)
				Refresh();
		}

		void Refresh() {
			ReleaseDeviceSubscription();

			currentAccount = LoadCurrentAccount();
			_deviceFactories.Clear();

			var mandev = Devices.Where(d => d.IsManual);
			var uris = new List<string>();

			mandev.ForEach(dev => {
				uris.Add(dev.ManualDevice.DevUri);
			});
			Devices.Clear();

			uris.ForEach(ur => {
				ManualLoaded(ur);
			});

			LoadDevices();
			PublishRefresh();
		}
		

		OdmSession facade;
		System.Net.NetworkCredential currentAccount = null;
		System.Net.NetworkCredential LoadCurrentAccount() {
			var acc = AccountManager.Instance.CurrentAccount;
			System.Net.NetworkCredential account = null;
			if (!acc.IsAnonymous)
                account = new System.Net.NetworkCredential() { UserName = acc.Name, Password = acc.Password };
			
			return account;
		}
		System.Net.NetworkCredential GetCurrentAccount() {
			return currentAccount;
		}
		CompositeDisposable IdentitySubscriptions = new CompositeDisposable();

		public void LoadDevices() {
			deviceManager = new NvtManager();
			try {
				//currentAccount = LoadCurrentAccount();
				_sessionFactory = new NvtSessionFactory(currentAccount);
				discoverSubscription.Disposable = deviceManager
					 .Observe()
					 .ObserveOnDispatcher()
					 .Subscribe(node => {
						 OnNodeLoaded(node);
					 }, err => {
						 dbg.Error(err);
					 }, () => {
					 });
				deviceManager.Discover(TimeSpan.FromSeconds(20));

			} catch (Exception err) {
				dbg.Error(err);
				//MessageBox.Show(err.Message);
			}
		}

		void OnNodeLoaded(INvtNode node) {
			if (node.identity.uris.Count() != 0) {
				try {
					DeviceDescriptionHolder devHolder = new DeviceDescriptionHolder();
					var scopes = node.identity.scopes.Select(x => x.OriginalString);
					devHolder.Uris = node.identity.uris;
					
					devHolder.Address = "";

					devHolder.Uris.ForEach(x => {
						if (x.IsAbsoluteUri) {
							devHolder.Address += x.DnsSafeHost + "; ";
						} else {
							dbg.Error("Uri not supported");
						}
					});
					devHolder.Address = devHolder.Address.TrimEnd(new Char[] { ';', ' ' });
					if (devHolder.Address == "") {
						devHolder.IsInvalidUris = true;
						devHolder.Address = "Invalid Uri";
					}
					devHolder.Name = ScopeHelper.GetName(scopes);
					devHolder.Location = ScopeHelper.GetLocation(scopes);
					devHolder.DeviceIconUri = ScopeHelper.GetDeviceIconUri(scopes);
					devHolder.Account = GetCurrentAccount();
					SessionProcess(devHolder, false);

					Devices.Add(devHolder);

					subscriptions.Add(node.RegisterRemovalHandler(() => {
						currentDispatcher.BeginInvoke(() => {
							if (devHolder == SelectedDevice) {
								//Publish release ui event
								PublishRefresh();
							}
							Devices.Remove(devHolder);
						});
					}));
				} catch (Exception err) {
					dbg.Error(err);
				}
			}
		}

		
		NvtSessionFactory _sessionFactory;
		NvtSessionFactory sessionFactory {
			get {
				if (_sessionFactory == null)
					throw new ArgumentNullException("_sessionFactory");
				return _sessionFactory;
			}
		}
		void SessionProcess(DeviceDescriptionHolder devHolder, bool publishEvent) {
			TrySessionWithCredentials(devHolder, publishEvent);
		}

		/// <summary>
		/// Returns a cache key for the device — the host portion of its first URI.
		/// Returns null if no URIs are available.
		/// </summary>
		static string GetDeviceCacheKey(DeviceDescriptionHolder devHolder) {
			if (devHolder.Uris == null || devHolder.Uris.Count() == 0)
				return null;
			return devHolder.Uris[0].DnsSafeHost;
		}

		/// <summary>
		/// Iterates through all stored credentials to find one that authenticates with the device.
		/// Checks the credential cache first — if a cached credential exists for this device host,
		/// it is tried before falling back to full iteration. On any cache miss or failure the
		/// cache entry is evicted and all credentials are tried in order.
		/// </summary>
		void TrySessionWithCredentials(DeviceDescriptionHolder devHolder, bool publishEvent) {
			var cacheKey = GetDeviceCacheKey(devHolder);
			Account cachedAccount;
			if (cacheKey != null && _credentialCache.TryGetValue(cacheKey, out cachedAccount)) {
				// Cache hit — try cached credential first
				var cachedCred = cachedAccount.IsAnonymous
					? null
					: new System.Net.NetworkCredential() { UserName = cachedAccount.Name, Password = cachedAccount.Password };
				var cachedFactory = new NvtSessionFactory(cachedCred);
				IdentitySubscriptions.Add(cachedFactory.CreateSession(devHolder.Uris)
					.ObserveOnCurrentDispatcher()
					.Subscribe(session => {
						// Cached credential still works
						devHolder.Account = cachedCred;
						_deviceFactories[devHolder] = cachedFactory;
						InitDeviceHolder(session, devHolder, publishEvent);
					}, err => {
						// Cached credential failed — evict and fall through to full iteration
						_credentialCache.Remove(cacheKey);
						FullCredentialIteration(devHolder, publishEvent);
					}));
				return;
			}

			// No cache entry — full iteration
			FullCredentialIteration(devHolder, publishEvent);
		}

		void FullCredentialIteration(DeviceDescriptionHolder devHolder, bool publishEvent) {
			var allCreds = AccountManager.Instance.GetAllCredentials();
			var attempts = new List<System.Net.NetworkCredential>();

			foreach (var cred in allCreds) {
				if (!cred.IsAnonymous) {
					attempts.Add(new System.Net.NetworkCredential() { UserName = cred.Name, Password = cred.Password });
				}
			}
			// Anonymous (null credential) as final fallback
			attempts.Add(null);

			TryNextCredential(devHolder, attempts, 0, publishEvent);
		}

		void TryNextCredential(DeviceDescriptionHolder devHolder, List<System.Net.NetworkCredential> credentials, int index, bool publishEvent) {
			if (index >= credentials.Count) {
				// All credentials exhausted — last-resort fallback: anonymous on the last URI only
				var fallbackFactory = new NvtSessionFactory(null);
				_deviceFactories[devHolder] = fallbackFactory;
				devHolder.Account = null;
				CacheCredential(devHolder, Account.Anonymous);
				InitDeviceHolder(fallbackFactory.CreateSession(devHolder.Uris[devHolder.Uris.Count() - 1]), devHolder, publishEvent);
				return;
			}

			var cred = credentials[index];
			var factory = new NvtSessionFactory(cred);
			IdentitySubscriptions.Add(factory.CreateSession(devHolder.Uris)
				.ObserveOnCurrentDispatcher()
				.Subscribe(session => {
					// Success — store working credential and factory for this device, update cache
					devHolder.Account = cred;
					_deviceFactories[devHolder] = factory;
					CacheCredential(devHolder, new Account() { Name = cred.UserName, Password = cred.Password });
					InitDeviceHolder(session, devHolder, publishEvent);
				}, err => {
					// This credential failed — try next one
					TryNextCredential(devHolder, credentials, index + 1, publishEvent);
				}));
		}

		void CacheCredential(DeviceDescriptionHolder devHolder, Account account) {
			var cacheKey = GetDeviceCacheKey(devHolder);
			if (cacheKey != null) {
				_credentialCache[cacheKey] = account;
			}
		}

		void InitDeviceHolder(INvtSession session, DeviceDescriptionHolder devHolder, bool publish) {
			devHolder.session = session;
			facade = new OdmSession(session);
			var model = new IdentificationModel();
			IdentitySubscriptions.Add(
					  facade.GetIdentity(() => model)
							.ObserveOnCurrentDispatcher()
							.Subscribe(mod => {
								devHolder.Init(mod);
								if (publish)
									DeviceSelectedPublish(devHolder, sessionFactory);
							}, err => {
								//dbg.Error(err);
								//MessageBox.Show(err.Message);
							})
				 );
		}

		void DeviceSelectedPublish(DeviceDescriptionHolder dev, NvtSessionFactory factory) {
			var evargs = new DeviceSelectedEventArgs();
			NvtSessionFactory devFactory;
			evargs.sessionFactory = _deviceFactories.TryGetValue(dev, out devFactory) ? devFactory : factory;
			evargs.devHolder = dev;
			eventAggregator.GetEvent<DeviceSelectedEvent>().Publish(evargs);
		}

		#region Commands
		void InitCommands() {
			onRefresh = new DelegateCommand(() => {
				Refresh();
			});
			onAdd = new DelegateCommand(() => {
				ManualAdd();
			});
		}
		public ICommand onRefresh {
			get { return (ICommand)GetValue(onRefreshProperty); }
			set { SetValue(onRefreshProperty, value); }
		}
		// Using a DependencyProperty as the backing store for ErrorBtnClick.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty onRefreshProperty =
			DependencyProperty.Register("onRefresh", typeof(ICommand), typeof(DeviceListViewModel));

		public ICommand onAdd {
			get { return (ICommand)GetValue(onAddProperty); }
			set { SetValue(onAddProperty, value); }
		}
		// Using a DependencyProperty as the backing store for ErrorBtnClick.  This enables animation, styling, binding, etc...
		public static readonly DependencyProperty onAddProperty =
			DependencyProperty.Register("onAdd", typeof(ICommand), typeof(DeviceListViewModel));
		#endregion



		public string Filter {get { return (string)GetValue(FilterProperty); }set { SetValue(FilterProperty, value); }}
		public static readonly DependencyProperty FilterProperty =
			DependencyProperty.Register("Filter", typeof(string), typeof(DeviceListViewModel), new UIPropertyMetadata((o, e) => {
				var wm = o as DeviceListViewModel;
				wm.Devices.Filter = (string)e.NewValue;
			}));

		public ObservableCollection<DeviceDescriptionHolder> DisplayDevices { get { return (ObservableCollection<DeviceDescriptionHolder>)GetValue(DisplayDevicesProperty); } set { SetValue(DisplayDevicesProperty, value); } }
		public static readonly DependencyProperty DisplayDevicesProperty = DependencyProperty.Register("DisplayDevices", typeof(ObservableCollection<DeviceDescriptionHolder>), typeof(DeviceListViewModel));

		public DevicesObservableCollection Devices {get { return (DevicesObservableCollection)GetValue(DevicesProperty); } set { SetValue(DevicesProperty, value); }}
		public static readonly DependencyProperty DevicesProperty = DependencyProperty.Register("Devices", typeof(DevicesObservableCollection), typeof(DeviceListViewModel));

		public DeviceDescriptionHolder SelectedDevice {get { return (DeviceDescriptionHolder)GetValue(SelectedDeviceProperty); }set { SetValue(SelectedDeviceProperty, value); }}
		public static readonly DependencyProperty SelectedDeviceProperty =
				DependencyProperty.Register("SelectedDevice", typeof(DeviceDescriptionHolder), typeof(DeviceListViewModel), new FrameworkPropertyMetadata(null, (obj, ev) => {
					((DeviceListViewModel)obj).OnSelectedDeviceChanged();
				}));
		void OnSelectedDeviceChanged() {
			var dev = (DeviceDescriptionHolder)SelectedDevice;
			if (dev == null)
				return;
			if (dev.Uris.Count() == 0)
				return;

			DeviceSelectedEventArgs evargs = new DeviceSelectedEventArgs();
			evargs.devHolder = dev;
			NvtSessionFactory devFactory;
			evargs.sessionFactory = _deviceFactories.TryGetValue(dev, out devFactory) ? devFactory : sessionFactory;
			eventAggregator.GetEvent<DeviceSelectedEvent>().Publish(evargs);
		}

		public void Dispose() {
            if (BatchUpgradeSubscribtion != null)
                eventAggregator.GetEvent<UpgradeButchClick>().Unsubscribe(BatchUpgradeSubscribtion);
            if (BatchRestoreSubscribtion != null)
                eventAggregator.GetEvent<RestoreButchClick>().Unsubscribe(BatchRestoreSubscribtion);
            if (RefreshSubscribtion != null)
                eventAggregator.GetEvent<Refresh>().Unsubscribe(RefreshSubscribtion);
		}
	}
}
