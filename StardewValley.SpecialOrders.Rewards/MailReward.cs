using System;
using System.Collections.Generic;
using Netcode;

namespace StardewValley.SpecialOrders.Rewards
{
	public class MailReward : OrderReward
	{
		public NetBool noLetter = new NetBool(true);

		public NetStringList grantedMails = new NetStringList();

		public NetBool host = new NetBool(false);

		public override void InitializeNetFields()
		{
			base.InitializeNetFields();
			base.NetFields.AddField(noLetter, "noLetter").AddField(grantedMails, "grantedMails").AddField(host, "host");
		}

		public override void Load(SpecialOrder order, Dictionary<string, string> data)
		{
			string raw = order.Parse(data["MailReceived"]);
			grantedMails.AddRange(ArgUtility.SplitBySpace(raw));
			string rawValue;
			if (data.TryGetValue("NoLetter", out rawValue))
			{
				noLetter.Value = Convert.ToBoolean(order.Parse(rawValue));
			}
			if (data.TryGetValue("Host", out rawValue))
			{
				host.Value = Convert.ToBoolean(order.Parse(rawValue));
			}
		}

		public override void Grant()
		{
			foreach (string mail in grantedMails)
			{
				if (host.Value)
				{
					if (!Game1.IsMasterGame)
					{
						continue;
					}
					if (Game1.newDaySync.hasInstance())
					{
						Game1.addMail(mail, noLetter.Value, true);
						continue;
					}
					string actualMail2 = mail;
					if (actualMail2 == "ClintReward" && Game1.player.mailReceived.Contains("ClintReward"))
					{
						Game1.player.mailReceived.Remove("ClintReward2");
						actualMail2 = "ClintReward2";
					}
					Game1.addMailForTomorrow(actualMail2, noLetter.Value, true);
				}
				else if (Game1.newDaySync.hasInstance())
				{
					Game1.addMail(mail, noLetter.Value, true);
				}
				else
				{
					string actualMail = mail;
					if (actualMail == "ClintReward" && Game1.player.mailReceived.Contains("ClintReward"))
					{
						Game1.player.mailReceived.Remove("ClintReward2");
						actualMail = "ClintReward2";
					}
					Game1.addMailForTomorrow(actualMail, noLetter.Value, true);
				}
			}
		}
	}
}
