using CSharpDiscordWebhook.NET.Discord;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using System.Drawing;
using System.Net;

namespace OuterWildsBranchWatcher;

public class BranchInfo
{
	[JsonProperty("branchName")]
	public string BranchName = "";

	[JsonProperty("timeUpdated")]
	public int TimeUpdated;

	[JsonProperty("description")]
	public string Description = "";

	[JsonProperty("buildId")]
	public int BuildId = -1;

	[JsonProperty("pwdRequired")]
	public int PwdRequired = 0;
}

public class PriceInfo
{
	public int initialPrice = 0;
	public int currentPrice = 0;
	public int discountPercent = 0;
}

public class Program
{
	const string BUILDID = "buildid";
	const string DEPOTS = "depots";
	const string BRANCHES = "branches";
	const string TIMEUPDATED = "timeupdated";
	const string PWDREQUIRED = "pwdrequired";
	const string DESCRIPTION = "description";
	const string COMMON = "common";
	const string APP_NAME = "name";

	public static void Main(params string[] args)
	{
		var user = args[0];
		var pass = args[1];
		var webhook = args[2];
		var appid = uint.Parse(args[3]);

		var steamClient = new SteamClient();
		var manager = new CallbackManager(steamClient);

		var steamUser = steamClient.GetHandler<SteamUser>();
		var appHandler = steamClient.GetHandler<SteamApps>();

		manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
		manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
		manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
		manager.Subscribe<SteamApps.PICSProductInfoCallback>(OnPICSProductInfo); 

		var isRunning = true;
		Console.WriteLine($"Trying to connect to Steam...");
		steamClient.Connect();

		while (isRunning)
		{
			manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
		}

		void OnConnected(SteamClient.ConnectedCallback callback)
		{
			Console.WriteLine($"Connected to Steam. Logging on...");
			steamUser.LogOn(new SteamUser.LogOnDetails
			{
				Username = user,
				Password = pass,
			});
		}

		void OnDisconnected(SteamClient.DisconnectedCallback callback)
		{
			Console.WriteLine($"Disconnected from Steam.");
			isRunning = false;
		}

		async void OnLoggedOn(SteamUser.LoggedOnCallback callback)
		{
			if (callback.Result != EResult.OK)
			{
				Console.WriteLine($"Failed to log into Steam. Result:{callback.Result} ExtendedResult:{callback.Result}");
				isRunning = false;
				return;
			}

			Console.WriteLine($"Logged into Steam.");

			await appHandler.PICSGetProductInfo(new SteamApps.PICSRequest(appid), null, false);
		}

		void OnPICSProductInfo(SteamApps.PICSProductInfoCallback callback)
		{
			Console.WriteLine($"Recieved PICS data.");
			var item = callback.Apps.Single();

			var KeyValues = item.Value.KeyValues;

			var depots = KeyValues[DEPOTS];
			var branches = depots[BRANCHES];

			var common = KeyValues[COMMON];
			var appName = common[APP_NAME].Value;

			var newBranchInfoArray = new BranchInfo[branches.Children.Count];

			for (var i = 0; i < branches.Children.Count; i++)
			{
				var child = branches.Children[i];

				var timeupdated = child[TIMEUPDATED];

				newBranchInfoArray[i] = new BranchInfo() { BranchName = child.Name, TimeUpdated = int.Parse(timeupdated.Value), BuildId = int.Parse(child[BUILDID].Value)};

				if (child[DESCRIPTION] != KeyValue.Invalid)
				{
					newBranchInfoArray[i].Description = child[DESCRIPTION].Value;
				}

				if (child[PWDREQUIRED] != KeyValue.Invalid)
				{
					newBranchInfoArray[i].PwdRequired = int.Parse(child[PWDREQUIRED].Value);
				}
			}

			var newBranches = new List<BranchInfo>();
			var deletedBranches = new List<BranchInfo>();
			var updatedBranches = new List<BranchInfo>();

			if (!File.Exists("branches.json"))
			{
				File.WriteAllText("branches.json", JsonConvert.SerializeObject(new BranchInfo[] {}));
			}

			var previous = JsonConvert.DeserializeObject<BranchInfo[]>(File.ReadAllText("branches.json"));

			File.WriteAllText("branches.json", JsonConvert.SerializeObject(newBranchInfoArray));

			foreach (var newBranchInfo in newBranchInfoArray)
			{
				var existingBranch = previous.FirstOrDefault(x => x.BranchName == newBranchInfo.BranchName);

				if (existingBranch == default)
				{
					newBranches.Add(newBranchInfo);
				}
				else if (existingBranch.TimeUpdated != newBranchInfo.TimeUpdated)
				{
					updatedBranches.Add(newBranchInfo);
				}
			}

			foreach (var oldBranch in previous)
			{
				if (!newBranchInfoArray.Any(x => x.BranchName == oldBranch.BranchName))
				{
					deletedBranches.Add(oldBranch);
				}
			}

			// check for price update
			var json = new WebClient().DownloadString($"https://store.steampowered.com/api/appdetails?appids={appid}&cc=us&filters=price_overview");

			var jObject = JObject.Parse(json);

			var priceOverview = jObject[$"{appid}"]["data"]["price_overview"];
			var initialPrice = (int)priceOverview["initial"];
			var currentPrice = (int)priceOverview["final"];
			var discountPercent = (int)priceOverview["discount_percent"];

			if (!File.Exists("price.json"))
			{
				File.WriteAllText("price.json", JsonConvert.SerializeObject(new PriceInfo()));
			}

			var oldPrice = JsonConvert.DeserializeObject<PriceInfo>(File.ReadAllText("price.json"));
			var actualPriceHasChanged = initialPrice != oldPrice.initialPrice;
			var isOnSale = currentPrice != oldPrice.currentPrice;

			File.WriteAllText("price.json", JsonConvert.SerializeObject(new PriceInfo() { currentPrice = currentPrice, initialPrice = initialPrice, discountPercent = discountPercent}));

			if (newBranches.Count > 0 || updatedBranches.Count > 0)
			{
				Console.WriteLine($"Found changes - {newBranches.Count} new branches, {deletedBranches.Count} deleted branches, {updatedBranches.Count} updated branches.");

				var hook = new DiscordWebhook
				{
					Uri = new Uri(webhook)
				};

				var messageList = new List<DiscordMessage>();

				messageList.Add(new DiscordMessage());

				foreach (var newBranch in newBranches)
				{
					var embed = new DiscordEmbed
					{
						Title = "New Branch",
						Color = new DiscordColor(Color.Green),
						Description = $"The branch `{newBranch.BranchName}` was added at <t:{newBranch.TimeUpdated}:F>.",
						Fields = new List<EmbedField>(),
						Footer = new EmbedFooter() { Text = appName }
					};

					embed.Fields.Add(new EmbedField()
					{
						Name = "Name",
						Value = newBranch.BranchName,
						Inline = true
					});

					if (newBranch.Description != "")
					{
						embed.Fields.Add(new EmbedField()
						{
							Name = "Description",
							Value = newBranch.Description,
							Inline = true
						});
					}

					embed.Fields.Add(new EmbedField()
					{
						Name = "Password Locked",
						Value = newBranch.PwdRequired == 1 ? "Yes" : "No",
						Inline = true
					});

					embed.Fields.Add(new EmbedField()
					{
						Name = "BuildId",
						Value = newBranch.BuildId.ToString(),
						Inline = true
					});

					if (messageList.Last().Embeds.Count >= 10)
					{
						messageList.Add(new DiscordMessage());
					}

					messageList.Last().Embeds.Add(embed);
				}

				foreach (var deletedBranch in deletedBranches)
				{
					var embed = new DiscordEmbed
					{
						Title = "Deleted Branch",
						Color = new DiscordColor(Color.Red),
						Description = $"The branch `{deletedBranch.BranchName}` was deleted.",
						Fields = new List<EmbedField>(),
						Footer = new EmbedFooter() { Text = appName }
					};

					if (messageList.Last().Embeds.Count >= 10)
					{
						messageList.Add(new DiscordMessage());
					}

					messageList.Last().Embeds.Add(embed);
				}

				foreach (var updatedBranch in updatedBranches)
				{
					var embed = new DiscordEmbed
					{
						Title = "Updated Branch",
						Color = new DiscordColor(Color.Orange),
						Description = $"The branch `{updatedBranch.BranchName}` was updated at <t:{updatedBranch.TimeUpdated}:F>.",
						Fields = new List<EmbedField>(),
						Footer = new EmbedFooter() { Text = appName }
					};

					embed.Fields.Add(new EmbedField()
					{
						Name = "Name",
						Value = updatedBranch.BranchName,
						Inline = true
					});

					if (updatedBranch.Description != "")
					{
						embed.Fields.Add(new EmbedField()
						{
							Name = "Description",
							Value = updatedBranch.Description,
							Inline = true
						});
					}

					embed.Fields.Add(new EmbedField()
					{
						Name = "Password Locked",
						Value = updatedBranch.PwdRequired == 1 ? "Yes" : "No",
						Inline = true
					});

					embed.Fields.Add(new EmbedField()
					{
						Name = "BuildId",
						Value = updatedBranch.BuildId.ToString(),
						Inline = true
					});

					if (messageList.Last().Embeds.Count >= 10)
					{
						messageList.Add(new DiscordMessage());
					}

					messageList.Last().Embeds.Add(embed);
				}

				foreach (var message in messageList)
				{
					hook.SendAsync(message);
				}
			}

			if (actualPriceHasChanged || isOnSale)
			{
				var hook = new DiscordWebhook
				{
					Uri = new Uri(webhook)
				};

				var message = new DiscordMessage();

				if (actualPriceHasChanged)
				{
					var embed = new DiscordEmbed()
					{
						Title = "Price Change",
						Color = new DiscordColor(Color.LightBlue),
						Description = $"The base price has changed from ${oldPrice.initialPrice / 100f:F2} to ${initialPrice / 100f:F2}",
						Footer = new EmbedFooter() { Text = appName }
					};
					message.Embeds.Add(embed);
				}
				else
				{
					if (oldPrice.discountPercent == 0)
					{
						var embed = new DiscordEmbed()
						{
							Title = "Sale Started!",
							Color = new DiscordColor(Color.LightBlue),
							Description = $"A sale has started! From ${initialPrice / 100f:F2} to ${currentPrice / 100f:F2} ({discountPercent}% off).",
							Footer = new EmbedFooter() { Text = appName }
						};
						message.Embeds.Add(embed);
					}
					else if (currentPrice < oldPrice.currentPrice)
					{
						var embed = new DiscordEmbed()
						{
							Title = "Sale Update",
							Color = new DiscordColor(Color.LightBlue),
							Description = $"The sale has increased! From ${oldPrice.currentPrice / 100f:F2} ({oldPrice.discountPercent}% off) to ${currentPrice / 100f:F2} ({discountPercent}% off).",
							Footer = new EmbedFooter() { Text = appName }
						};
						message.Embeds.Add(embed);
					}
					else if (currentPrice == oldPrice.initialPrice)
					{
						var embed = new DiscordEmbed()
						{
							Title = "Sale Ended",
							Color = new DiscordColor(Color.LightBlue),
							Description = $"The sale has ended. Back to ${initialPrice / 100f:F2}.",
							Footer = new EmbedFooter() { Text = appName }
						};
						message.Embeds.Add(embed);
					}
					else if (currentPrice > oldPrice.currentPrice)
					{
						var embed = new DiscordEmbed()
						{
							Title = "Sale Update",
							Color = new DiscordColor(Color.LightBlue),
							Description = $"The sale has decreased. From ${oldPrice.currentPrice / 100f:F2} ({oldPrice.discountPercent}% off) to ${currentPrice / 100f:F2} ({discountPercent}% off).",
							Footer = new EmbedFooter() { Text = appName }
						};
						message.Embeds.Add(embed);
					}
				}

				hook.SendAsync(message);
			}

			steamUser.LogOff();
		}
	}
}