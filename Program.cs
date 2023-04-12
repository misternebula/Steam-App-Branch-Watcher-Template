using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using SteamKit2;
using static SteamKit2.Internal.PublishedFileDetails;

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
}

public class Program
{
	public const int OW_APPID = 753640;

	public static void Main(params string[] args)
	{
		if (args.Length < 4)
		{
			Console.WriteLine($"Incorrect number of arguments. Expected 4, found {args.Length}");
			return;
		}

		var user = args[0];
		var pass = args[1];

		Console.WriteLine($"username length : {user.Length}");
		Console.WriteLine($"password length : {pass.Length}");

		var previous = JsonConvert.DeserializeObject<BranchInfo[]>(args[2]);

		var discordToken = args[3];

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
			Console.WriteLine($"Received LoggedOn callback...");

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

			var depots = KeyValues["depots"];
			var branches = depots["branches"];

			var newBranchInfoArray = new BranchInfo[branches.Children.Count];

			for (var i = 0; i < branches.Children.Count; i++)
			{
				var child = branches.Children[i];

				var timeupdated = child["timeupdated"];

				newBranchInfoArray[i] = new BranchInfo() { BranchName = child.Name, TimeUpdated = int.Parse(timeupdated.Value), BuildId = int.Parse(child["buildid"].Value) };

				if (child["description"] != KeyValue.Invalid)
				{
					newBranchInfoArray[i].Description = child["description"].Value;
				}
			}

			Console.WriteLine(JsonConvert.SerializeObject(newBranchInfoArray));

			var newBranches = new List<BranchInfo>();
			var deletedBranches = new List<BranchInfo>();
			var updatedBranches = new List<BranchInfo>();

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

			Console.WriteLine(Directory.GetCurrentDirectory());

			foreach (var item2 in Directory.GetFiles("../../../branches.json"))
			{
				Console.WriteLine(item2);
			}

			Console.WriteLine(File.Exists("../../../branches.json"));

			previous = JsonConvert.DeserializeObject<BranchInfo[]>(File.ReadAllText("../../../branches.json"));

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
			//var channel = guild.GetTextChannel(939053638310064138); // #outer-wilds-chat
			var channel = guild.GetTextChannel(1057602032850186300); // #test-channel

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