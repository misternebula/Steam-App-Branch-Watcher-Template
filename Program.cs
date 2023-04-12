using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using SteamKit2;

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

public class Program
{
	public const int OW_APPID = 753640;

	const string BUILDID = "buildid";
	const string DEPOTS = "depots";
	const string BRANCHES = "branches";
	const string TIMEUPDATED = "timeupdated";
	const string PWDREQUIRED = "pwdrequired";
	const string DESCRIPTION = "description";

	public static void Main(params string[] args)
	{
		var user = args[0];
		var pass = args[1];
		var discordToken = args[2];

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

			await appHandler.PICSGetProductInfo(new SteamApps.PICSRequest(OW_APPID), null, false);
		}

		void OnPICSProductInfo(SteamApps.PICSProductInfoCallback callback)
		{
			Console.WriteLine($"Recieved PICS data.");
			var item = callback.Apps.Single();

			var KeyValues = item.Value.KeyValues;

			var depots = KeyValues[DEPOTS];
			var branches = depots[BRANCHES];

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

			if (newBranches.Count > 0 || updatedBranches.Count > 0)
			{
				Console.WriteLine($"Found changes.");
				new Program().MainAsync(discordToken, newBranches, deletedBranches, updatedBranches).Wait();
			}
			else
			{
				Console.WriteLine($"No changes found.");
			}

			steamUser.LogOff();
		}
	}

	public async Task MainAsync(string discordToken, List<BranchInfo> newBranches, List<BranchInfo> deletedBranches, List<BranchInfo> updatedBranches)
	{
		var client = new DiscordSocketClient();
		client.Log += Log;
		await client.LoginAsync(TokenType.Bot, discordToken);
		await client.StartAsync();

		client.Ready += async () =>
		{
			var guild = client.GetGuild(929708786027999262);
			var channel = guild.GetTextChannel(939053638310064138); // #outer-wilds-chat

			List<Embed> embeds = new();

			foreach (var item in newBranches)
			{
				var newEmbed = new EmbedBuilder()
				{
					Title = "New Branch",
					Color = Color.Green,
					Description = $"The branch `{item.BranchName}` was added at <t:{item.TimeUpdated}:F>."
				};

				newEmbed.Fields.Add(new EmbedFieldBuilder()
				{
					Name = "Name",
					Value = item.BranchName,
					IsInline = true
				});

				if (item.Description != "")
				{
					newEmbed.Fields.Add(new EmbedFieldBuilder()
					{
						Name = "Description",
						Value = item.Description,
						IsInline = true
					});
				}

				newEmbed.Fields.Add(new EmbedFieldBuilder()
				{
					Name = "Password Locked",
					Value = item.PwdRequired == 1 ? "Yes" : "No",
					IsInline = true
				});

				newEmbed.Fields.Add(new EmbedFieldBuilder()
				{
					Name = "BuildId",
					Value = item.BuildId,
					IsInline = true
				});

				embeds.Add(newEmbed.Build());
			}

			foreach (var item in deletedBranches)
			{
				var newEmbed = new EmbedBuilder()
				{
					Title = "Deleted Branch",
					Color = Color.Red,
					Description = $"The branch `{item.BranchName}` was deleted."
				};

				embeds.Add(newEmbed.Build());
			}

			foreach (var item in updatedBranches)
			{
				var newEmbed = new EmbedBuilder()
				{
					Title = "Updated Branch",
					Color = Color.Orange,
					Description = $"The branch `{item.BranchName}` was updated at <t:{item.TimeUpdated}:F>."
				};

				newEmbed.Fields.Add(new EmbedFieldBuilder()
				{
					Name = "Name",
					Value = item.BranchName,
					IsInline = true
				});

				if (item.Description != "")
				{
					newEmbed.Fields.Add(new EmbedFieldBuilder()
					{
						Name = "Description",
						Value = item.Description,
						IsInline = true
					});
				}

				newEmbed.Fields.Add(new EmbedFieldBuilder()
				{
					Name = "Password Locked",
					Value = item.PwdRequired == 1 ? "Yes" : "No",
					IsInline = true
				});

				newEmbed.Fields.Add(new EmbedFieldBuilder()
				{
					Name = "BuildId",
					Value = item.BuildId,
					IsInline = true
				});

				embeds.Add(newEmbed.Build());
			}

			await channel.SendMessageAsync(null, embeds: embeds.ToArray());

			await client.LogoutAsync();
			Environment.Exit(0);
		};

		await Task.Delay(-1);
	}

	private Task Log(LogMessage msg)
	{
		Console.WriteLine(msg.ToString());
		return Task.CompletedTask;
	}
}