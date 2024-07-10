using System.Collections.Generic;

namespace Netcode
{
	public class NetClock
	{
		public NetVersion netVersion;

		public int LocalId;

		public int InterpolationTicks;

		public List<bool> blanks = new List<bool>();

		public NetClock()
		{
			netVersion = default(NetVersion);
			LocalId = AddNewPeer();
		}

		public int AddNewPeer()
		{
			int id = blanks.IndexOf(true);
			if (id != -1)
			{
				blanks[id] = false;
			}
			else
			{
				id = netVersion.Size();
				while (blanks.Count < netVersion.Size())
				{
					blanks.Add(false);
				}
				netVersion[id] = 0u;
			}
			return id;
		}

		public void RemovePeer(int id)
		{
			while (blanks.Count <= id)
			{
				blanks.Add(false);
			}
			blanks[id] = true;
		}

		public uint GetLocalTick()
		{
			return netVersion[LocalId];
		}

		public void Tick()
		{
			netVersion[LocalId]++;
		}

		public void Clear()
		{
			netVersion.Clear();
			LocalId = 0;
		}

		public override string ToString()
		{
			return base.ToString() + ";LocalId=" + LocalId;
		}
	}
}
