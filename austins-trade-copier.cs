// TradeCopier with per-target cross-micro checkboxes and ratio support
#region Using declarations
using System;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
	public class TradeCopier : NinjaTrader.NinjaScript.AddOnBase
	{
		private TradeCopierWindow window;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Multi-Account Trade Copier Addon";
				Name = "Austin's Trade Copier";
			}
			else if (State == State.Active)
			{
				if (window == null || !window.IsVisible)
				{
					window = new TradeCopierWindow();
					window.Show();
				}
				else
				{
					window.Focus();
				}
			}
			else if (State == State.Terminated)
			{
				if (window != null)
				{
					window.Close();
					window = null;
				}
			}
		}
	}

	public class TradeCopierWindow : NTWindow
	{
		private Account leadAccount;
		private ComboBox leadAccountComboBox;
		private StackPanel targetAccountsPanel;
		private Button addAccountButton;
		private Button startStopButton;
		private Button flattenAllButton;
		private bool isCopying = false;

		private class TargetRow
		{
			public Account Account;
			public CheckBox CrossToMicro;
			public TextBox RatioBox;
		}

		private readonly List<TargetRow> targetRows = new List<TargetRow>();

		public TradeCopierWindow()
		{
			Caption = "Austin's Trade Copier";
			Width = 400;
			Height = 550;

			Topmost = true; // 👈 This makes the window stay on top

			CreateUI();
			RefreshAccountList();

			Account.AccountStatusUpdate += OnAccountStatusUpdate;
			Closing += TradeCopierWindow_Closing;
		}

		private void CreateUI()
		{
			var grid = new Grid();
			for (int i = 0; i < 6; i++)
				grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			// Lead
			var leadPanel = new StackPanel { Margin = new Thickness(10) };
			leadPanel.Children.Add(new Label { Content = "Lead Account:", FontWeight = FontWeights.Bold, Foreground = Brushes.White });
			leadAccountComboBox = new ComboBox { Margin = new Thickness(0, 5, 0, 10), Padding = new Thickness(5), MinWidth = 200 };
			leadAccountComboBox.SelectionChanged += LeadAccountComboBox_SelectionChanged;
			leadPanel.Children.Add(leadAccountComboBox);
			Grid.SetRow(leadPanel, 0);
			grid.Children.Add(leadPanel);

			grid.Children.Add(new Label { Content = "Target Accounts:", FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(10, 0, 10, 5) });
			Grid.SetRow(grid.Children[grid.Children.Count - 1], 1);

			targetAccountsPanel = new StackPanel { Margin = new Thickness(10, 0, 10, 10) };
			var scroll = new ScrollViewer { Content = targetAccountsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
			Grid.SetRow(scroll, 2);
			grid.Children.Add(scroll);

			addAccountButton = new Button { Content = "Add Target Account", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(10) };
			addAccountButton.Click += AddAccountButton_Click;
			Grid.SetRow(addAccountButton, 3);
			grid.Children.Add(addAccountButton);

			startStopButton = new Button { Content = "Start Copying", Padding = new Thickness(10), Margin = new Thickness(10) };
			startStopButton.Click += StartStopButton_Click;
			Grid.SetRow(startStopButton, 4);
			grid.Children.Add(startStopButton);

			flattenAllButton = new Button { Content = "Flatten All Accounts", Padding = new Thickness(10), Margin = new Thickness(10) };
			flattenAllButton.Click += FlattenAllButton_Click;
			Grid.SetRow(flattenAllButton, 5);
			grid.Children.Add(flattenAllButton);

			Content = grid;
			Background = Brushes.DarkGray;
		}

		private void AddAccountButton_Click(object sender, RoutedEventArgs e)
		{
			var row = new TargetRow();

			var horizontalStackPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };

			var cb = new ComboBox
			{
				DisplayMemberPath = "Name",
				ItemsSource = leadAccountComboBox.ItemsSource,
				MinWidth = 140,
				Margin = new Thickness(0, 0, 5, 0)
			};
			cb.SelectionChanged += (s, ev) => row.Account = cb.SelectedItem as Account;

			var crossCheck = new CheckBox { Content = "Micro", Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
			row.CrossToMicro = crossCheck;

			var ratioBox = new TextBox { Width = 30, Text = "10", Margin = new Thickness(0, 0, 5, 0) };
			row.RatioBox = ratioBox;

			var removeButton = new Button { Content = "X", Width = 25, Height = 25 };
			removeButton.Click += (s, args) => { targetRows.Remove(row); targetAccountsPanel.Children.Remove(horizontalStackPanel); };

			horizontalStackPanel.Children.Add(cb);
			horizontalStackPanel.Children.Add(crossCheck);
			horizontalStackPanel.Children.Add(ratioBox);
			horizontalStackPanel.Children.Add(removeButton);

			targetAccountsPanel.Children.Add(horizontalStackPanel);
			targetRows.Add(row);
		}

		private void RefreshAccountList()
		{
			var accounts = Account.All.Where(a => a.ConnectionStatus == ConnectionStatus.Connected).ToList();
			Dispatcher.InvokeAsync(() =>
			{
				leadAccountComboBox.ItemsSource = accounts;
				foreach (var sp in targetAccountsPanel.Children.OfType<StackPanel>())
				{
					var cb = sp.Children.OfType<ComboBox>().FirstOrDefault();
					if (cb != null)
					{
						var selected = cb.SelectedItem as Account;
						cb.ItemsSource = accounts;
						cb.SelectedItem = selected;
					}
				}
			});
		}

		private void OnAccountStatusUpdate(object sender, AccountStatusEventArgs e) => RefreshAccountList();

		private void LeadAccountComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (leadAccount != null) leadAccount.OrderUpdate -= OnOrderUpdate;
			leadAccount = leadAccountComboBox.SelectedItem as Account;
			if (leadAccount != null) leadAccount.OrderUpdate += OnOrderUpdate;
		}

		private void StartStopButton_Click(object sender, RoutedEventArgs e)
		{
			if (!isCopying)
			{
				if (leadAccount == null || targetRows.Count == 0 || targetRows.Any(r => r.Account == leadAccount))
				{
					MessageBox.Show("Please select valid lead and target accounts.");
					return;
				}
				isCopying = true;
				startStopButton.Content = "Stop Copying";
			}
			else
			{
				isCopying = false;
				startStopButton.Content = "Start Copying";
				FlattenAllPositions();
			}
		}

		private void OnOrderUpdate(object sender, OrderEventArgs args)
		{
			if (!isCopying || args.Order.Account != leadAccount || args.Order.OrderState != OrderState.Filled) return;
			Dispatcher.InvokeAsync(() => CopyOrderToTargets(args.Order));
		}

		private void CopyOrderToTargets(Order sourceOrder)
		{
			foreach (var row in targetRows)
			{
				if (row.Account == null) continue;

				Instrument instrumentToUse = sourceOrder.Instrument;
				int qty = sourceOrder.Filled;

				if (row.CrossToMicro.IsChecked == true)
				{
					try
					{
						string microSymbol = "M" + instrumentToUse.FullName;
						instrumentToUse = Instrument.GetInstrument(microSymbol);
						int ratio = int.TryParse(row.RatioBox.Text, out int r) ? r : 1;
						qty *= ratio;
					}
					catch (Exception ex)
					{
						MessageBox.Show("Cross instrument error: " + ex.Message);
						continue;
					}
				}

				try
				{
					var newOrder = new Order
					{
						Account = row.Account,
						OrderType = sourceOrder.OrderType,
						Instrument = instrumentToUse,
						Quantity = qty,
						LimitPrice = sourceOrder.LimitPrice,
						StopPrice = sourceOrder.StopPrice,
						OrderAction = sourceOrder.OrderAction
					};
					row.Account.Submit(new[] { newOrder });
				}
				catch (Exception ex)
				{
					MessageBox.Show("Error submitting to " + row.Account.Name + ": " + ex.Message);
				}
			}
		}

		private void FlattenAllButton_Click(object sender, RoutedEventArgs e) => FlattenAllPositions();

		private void FlattenAllPositions()
		{
			var accounts = targetRows.Select(r => r.Account).ToList();
			if (leadAccount != null) accounts.Add(leadAccount);
			foreach (var acct in accounts.Distinct())
			{
				CancelAllOrders(acct);
				CloseAllPositions(acct);
				VerifyAndForceClosePositions(acct);
			}
			MessageBox.Show("All positions flattened.");
		}

		private void CancelAllOrders(Account account)
		{
			foreach (Order o in account.Orders)
			{
				if (o.OrderState == OrderState.Working)
				{
					try { account.Cancel(new[] { o }); } catch { }
				}
			}
		}

		private void CloseAllPositions(Account account)
		{
			foreach (Position p in account.Positions)
			{
				if (p.Quantity != 0)
				{
					var action = p.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
					var close = account.CreateOrder(p.Instrument, action, OrderType.Market, TimeInForce.Day, Math.Abs(p.Quantity), 0, 0, string.Empty, "Close", null);
					account.Submit(new[] { close });
				}
			}
		}

		private void VerifyAndForceClosePositions(Account account)
		{
			System.Threading.Thread.Sleep(1000);
			foreach (Position p in account.Positions)
			{
				if (p.Quantity != 0)
				{
					var action = p.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
					var close = account.CreateOrder(p.Instrument, action, OrderType.Market, TimeInForce.Day, Math.Abs(p.Quantity), 0, 0, string.Empty, "Force Close", null);
					account.Submit(new[] { close });
				}
			}
		}

		private void TradeCopierWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			StopCopyingTrades();
			if (leadAccount != null)
				leadAccount.OrderUpdate -= OnOrderUpdate;
			Account.AccountStatusUpdate -= OnAccountStatusUpdate;
		}

		private void StopCopyingTrades()
		{
			isCopying = false;
			startStopButton.Content = "Start Copying";
		}
	}
}
