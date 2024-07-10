using System;

namespace Ionic.Zlib
{
	internal sealed class InflateCodes
	{
		private const int START = 0;

		private const int LEN = 1;

		private const int LENEXT = 2;

		private const int DIST = 3;

		private const int DISTEXT = 4;

		private const int COPY = 5;

		private const int LIT = 6;

		private const int WASH = 7;

		private const int END = 8;

		private const int BADCODE = 9;

		internal int mode;

		internal int len;

		internal int[] tree;

		internal int tree_index;

		internal int need;

		internal int lit;

		internal int bitsToGet;

		internal int dist;

		internal byte lbits;

		internal byte dbits;

		internal int[] ltree;

		internal int ltree_index;

		internal int[] dtree;

		internal int dtree_index;

		internal InflateCodes()
		{
		}

		internal void Init(int bl, int bd, int[] tl, int tl_index, int[] td, int td_index)
		{
			mode = 0;
			lbits = (byte)bl;
			dbits = (byte)bd;
			ltree = tl;
			ltree_index = tl_index;
			dtree = td;
			dtree_index = td_index;
			tree = null;
		}

		internal int Process(InflateBlocks blocks, int r)
		{
			int b = 0;
			int j = 0;
			int p = 0;
			ZlibCodec z = blocks._codec;
			p = z.NextIn;
			int l = z.AvailableBytesIn;
			b = blocks.bitb;
			j = blocks.bitk;
			int q = blocks.writeAt;
			int k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
			while (true)
			{
				switch (mode)
				{
				case 0:
					if (k >= 258 && l >= 10)
					{
						blocks.bitb = b;
						blocks.bitk = j;
						z.AvailableBytesIn = l;
						z.TotalBytesIn += p - z.NextIn;
						z.NextIn = p;
						blocks.writeAt = q;
						r = InflateFast(lbits, dbits, ltree, ltree_index, dtree, dtree_index, blocks, z);
						p = z.NextIn;
						l = z.AvailableBytesIn;
						b = blocks.bitb;
						j = blocks.bitk;
						q = blocks.writeAt;
						k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
						if (r != 0)
						{
							mode = ((r == 1) ? 7 : 9);
							break;
						}
					}
					need = lbits;
					tree = ltree;
					tree_index = ltree_index;
					mode = 1;
					goto case 1;
				case 1:
				{
					int i;
					for (i = need; j < i; j += 8)
					{
						if (l != 0)
						{
							r = 0;
							l--;
							b |= (z.InputBuffer[p++] & 0xFF) << j;
							continue;
						}
						blocks.bitb = b;
						blocks.bitk = j;
						z.AvailableBytesIn = l;
						z.TotalBytesIn += p - z.NextIn;
						z.NextIn = p;
						blocks.writeAt = q;
						return blocks.Flush(r);
					}
					int tindex = (tree_index + (b & InternalInflateConstants.InflateMask[i])) * 3;
					b >>= tree[tindex + 1];
					j -= tree[tindex + 1];
					int e = tree[tindex];
					if (e == 0)
					{
						lit = tree[tindex + 2];
						mode = 6;
						break;
					}
					if (((uint)e & 0x10u) != 0)
					{
						bitsToGet = e & 0xF;
						len = tree[tindex + 2];
						mode = 2;
						break;
					}
					if ((e & 0x40) == 0)
					{
						need = e;
						tree_index = tindex / 3 + tree[tindex + 2];
						break;
					}
					if (((uint)e & 0x20u) != 0)
					{
						mode = 7;
						break;
					}
					mode = 9;
					z.Message = "invalid literal/length code";
					r = -3;
					blocks.bitb = b;
					blocks.bitk = j;
					z.AvailableBytesIn = l;
					z.TotalBytesIn += p - z.NextIn;
					z.NextIn = p;
					blocks.writeAt = q;
					return blocks.Flush(r);
				}
				case 2:
				{
					int i;
					for (i = bitsToGet; j < i; j += 8)
					{
						if (l != 0)
						{
							r = 0;
							l--;
							b |= (z.InputBuffer[p++] & 0xFF) << j;
							continue;
						}
						blocks.bitb = b;
						blocks.bitk = j;
						z.AvailableBytesIn = l;
						z.TotalBytesIn += p - z.NextIn;
						z.NextIn = p;
						blocks.writeAt = q;
						return blocks.Flush(r);
					}
					len += b & InternalInflateConstants.InflateMask[i];
					b >>= i;
					j -= i;
					need = dbits;
					tree = dtree;
					tree_index = dtree_index;
					mode = 3;
					goto case 3;
				}
				case 3:
				{
					int i;
					for (i = need; j < i; j += 8)
					{
						if (l != 0)
						{
							r = 0;
							l--;
							b |= (z.InputBuffer[p++] & 0xFF) << j;
							continue;
						}
						blocks.bitb = b;
						blocks.bitk = j;
						z.AvailableBytesIn = l;
						z.TotalBytesIn += p - z.NextIn;
						z.NextIn = p;
						blocks.writeAt = q;
						return blocks.Flush(r);
					}
					int tindex = (tree_index + (b & InternalInflateConstants.InflateMask[i])) * 3;
					b >>= tree[tindex + 1];
					j -= tree[tindex + 1];
					int e = tree[tindex];
					if (((uint)e & 0x10u) != 0)
					{
						bitsToGet = e & 0xF;
						dist = tree[tindex + 2];
						mode = 4;
						break;
					}
					if ((e & 0x40) == 0)
					{
						need = e;
						tree_index = tindex / 3 + tree[tindex + 2];
						break;
					}
					mode = 9;
					z.Message = "invalid distance code";
					r = -3;
					blocks.bitb = b;
					blocks.bitk = j;
					z.AvailableBytesIn = l;
					z.TotalBytesIn += p - z.NextIn;
					z.NextIn = p;
					blocks.writeAt = q;
					return blocks.Flush(r);
				}
				case 4:
				{
					int i;
					for (i = bitsToGet; j < i; j += 8)
					{
						if (l != 0)
						{
							r = 0;
							l--;
							b |= (z.InputBuffer[p++] & 0xFF) << j;
							continue;
						}
						blocks.bitb = b;
						blocks.bitk = j;
						z.AvailableBytesIn = l;
						z.TotalBytesIn += p - z.NextIn;
						z.NextIn = p;
						blocks.writeAt = q;
						return blocks.Flush(r);
					}
					dist += b & InternalInflateConstants.InflateMask[i];
					b >>= i;
					j -= i;
					mode = 5;
					goto case 5;
				}
				case 5:
				{
					int f;
					for (f = q - dist; f < 0; f += blocks.end)
					{
					}
					while (len != 0)
					{
						if (k == 0)
						{
							if (q == blocks.end && blocks.readAt != 0)
							{
								q = 0;
								k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
							}
							if (k == 0)
							{
								blocks.writeAt = q;
								r = blocks.Flush(r);
								q = blocks.writeAt;
								k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
								if (q == blocks.end && blocks.readAt != 0)
								{
									q = 0;
									k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
								}
								if (k == 0)
								{
									blocks.bitb = b;
									blocks.bitk = j;
									z.AvailableBytesIn = l;
									z.TotalBytesIn += p - z.NextIn;
									z.NextIn = p;
									blocks.writeAt = q;
									return blocks.Flush(r);
								}
							}
						}
						blocks.window[q++] = blocks.window[f++];
						k--;
						if (f == blocks.end)
						{
							f = 0;
						}
						len--;
					}
					mode = 0;
					break;
				}
				case 6:
					if (k == 0)
					{
						if (q == blocks.end && blocks.readAt != 0)
						{
							q = 0;
							k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
						}
						if (k == 0)
						{
							blocks.writeAt = q;
							r = blocks.Flush(r);
							q = blocks.writeAt;
							k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
							if (q == blocks.end && blocks.readAt != 0)
							{
								q = 0;
								k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
							}
							if (k == 0)
							{
								blocks.bitb = b;
								blocks.bitk = j;
								z.AvailableBytesIn = l;
								z.TotalBytesIn += p - z.NextIn;
								z.NextIn = p;
								blocks.writeAt = q;
								return blocks.Flush(r);
							}
						}
					}
					r = 0;
					blocks.window[q++] = (byte)lit;
					k--;
					mode = 0;
					break;
				case 7:
					if (j > 7)
					{
						j -= 8;
						l++;
						p--;
					}
					blocks.writeAt = q;
					r = blocks.Flush(r);
					q = blocks.writeAt;
					k = ((q < blocks.readAt) ? (blocks.readAt - q - 1) : (blocks.end - q));
					if (blocks.readAt != blocks.writeAt)
					{
						blocks.bitb = b;
						blocks.bitk = j;
						z.AvailableBytesIn = l;
						z.TotalBytesIn += p - z.NextIn;
						z.NextIn = p;
						blocks.writeAt = q;
						return blocks.Flush(r);
					}
					mode = 8;
					goto case 8;
				case 8:
					r = 1;
					blocks.bitb = b;
					blocks.bitk = j;
					z.AvailableBytesIn = l;
					z.TotalBytesIn += p - z.NextIn;
					z.NextIn = p;
					blocks.writeAt = q;
					return blocks.Flush(r);
				case 9:
					r = -3;
					blocks.bitb = b;
					blocks.bitk = j;
					z.AvailableBytesIn = l;
					z.TotalBytesIn += p - z.NextIn;
					z.NextIn = p;
					blocks.writeAt = q;
					return blocks.Flush(r);
				default:
					r = -2;
					blocks.bitb = b;
					blocks.bitk = j;
					z.AvailableBytesIn = l;
					z.TotalBytesIn += p - z.NextIn;
					z.NextIn = p;
					blocks.writeAt = q;
					return blocks.Flush(r);
				}
			}
		}

		internal int InflateFast(int bl, int bd, int[] tl, int tl_index, int[] td, int td_index, InflateBlocks s, ZlibCodec z)
		{
			int p = z.NextIn;
			int k = z.AvailableBytesIn;
			int b = s.bitb;
			int i = s.bitk;
			int q = s.writeAt;
			int j = ((q < s.readAt) ? (s.readAt - q - 1) : (s.end - q));
			int ml = InternalInflateConstants.InflateMask[bl];
			int md = InternalInflateConstants.InflateMask[bd];
			int c;
			while (true)
			{
				if (i < 20)
				{
					k--;
					b |= (z.InputBuffer[p++] & 0xFF) << i;
					i += 8;
					continue;
				}
				int t = b & ml;
				int[] tp = tl;
				int tp_index = tl_index;
				int tp_index_t_3 = (tp_index + t) * 3;
				int e;
				if ((e = tp[tp_index_t_3]) == 0)
				{
					b >>= tp[tp_index_t_3 + 1];
					i -= tp[tp_index_t_3 + 1];
					s.window[q++] = (byte)tp[tp_index_t_3 + 2];
					j--;
				}
				else
				{
					while (true)
					{
						b >>= tp[tp_index_t_3 + 1];
						i -= tp[tp_index_t_3 + 1];
						if (((uint)e & 0x10u) != 0)
						{
							e &= 0xF;
							c = tp[tp_index_t_3 + 2] + (b & InternalInflateConstants.InflateMask[e]);
							b >>= e;
							for (i -= e; i < 15; i += 8)
							{
								k--;
								b |= (z.InputBuffer[p++] & 0xFF) << i;
							}
							t = b & md;
							tp = td;
							tp_index = td_index;
							tp_index_t_3 = (tp_index + t) * 3;
							e = tp[tp_index_t_3];
							while (true)
							{
								b >>= tp[tp_index_t_3 + 1];
								i -= tp[tp_index_t_3 + 1];
								if (((uint)e & 0x10u) != 0)
								{
									break;
								}
								if ((e & 0x40) == 0)
								{
									t += tp[tp_index_t_3 + 2];
									t += b & InternalInflateConstants.InflateMask[e];
									tp_index_t_3 = (tp_index + t) * 3;
									e = tp[tp_index_t_3];
									continue;
								}
								z.Message = "invalid distance code";
								c = z.AvailableBytesIn - k;
								c = ((i >> 3 < c) ? (i >> 3) : c);
								k += c;
								p -= c;
								i -= c << 3;
								s.bitb = b;
								s.bitk = i;
								z.AvailableBytesIn = k;
								z.TotalBytesIn += p - z.NextIn;
								z.NextIn = p;
								s.writeAt = q;
								return -3;
							}
							for (e &= 0xF; i < e; i += 8)
							{
								k--;
								b |= (z.InputBuffer[p++] & 0xFF) << i;
							}
							int d = tp[tp_index_t_3 + 2] + (b & InternalInflateConstants.InflateMask[e]);
							b >>= e;
							i -= e;
							j -= c;
							int r;
							if (q >= d)
							{
								r = q - d;
								if (q - r > 0 && 2 > q - r)
								{
									s.window[q++] = s.window[r++];
									s.window[q++] = s.window[r++];
									c -= 2;
								}
								else
								{
									Array.Copy(s.window, r, s.window, q, 2);
									q += 2;
									r += 2;
									c -= 2;
								}
							}
							else
							{
								r = q - d;
								do
								{
									r += s.end;
								}
								while (r < 0);
								e = s.end - r;
								if (c > e)
								{
									c -= e;
									if (q - r > 0 && e > q - r)
									{
										do
										{
											s.window[q++] = s.window[r++];
										}
										while (--e != 0);
									}
									else
									{
										Array.Copy(s.window, r, s.window, q, e);
										q += e;
										r += e;
										e = 0;
									}
									r = 0;
								}
							}
							if (q - r > 0 && c > q - r)
							{
								do
								{
									s.window[q++] = s.window[r++];
								}
								while (--c != 0);
								break;
							}
							Array.Copy(s.window, r, s.window, q, c);
							q += c;
							r += c;
							c = 0;
							break;
						}
						if ((e & 0x40) == 0)
						{
							t += tp[tp_index_t_3 + 2];
							t += b & InternalInflateConstants.InflateMask[e];
							tp_index_t_3 = (tp_index + t) * 3;
							if ((e = tp[tp_index_t_3]) == 0)
							{
								b >>= tp[tp_index_t_3 + 1];
								i -= tp[tp_index_t_3 + 1];
								s.window[q++] = (byte)tp[tp_index_t_3 + 2];
								j--;
								break;
							}
							continue;
						}
						if (((uint)e & 0x20u) != 0)
						{
							c = z.AvailableBytesIn - k;
							c = ((i >> 3 < c) ? (i >> 3) : c);
							k += c;
							p -= c;
							i -= c << 3;
							s.bitb = b;
							s.bitk = i;
							z.AvailableBytesIn = k;
							z.TotalBytesIn += p - z.NextIn;
							z.NextIn = p;
							s.writeAt = q;
							return 1;
						}
						z.Message = "invalid literal/length code";
						c = z.AvailableBytesIn - k;
						c = ((i >> 3 < c) ? (i >> 3) : c);
						k += c;
						p -= c;
						i -= c << 3;
						s.bitb = b;
						s.bitk = i;
						z.AvailableBytesIn = k;
						z.TotalBytesIn += p - z.NextIn;
						z.NextIn = p;
						s.writeAt = q;
						return -3;
					}
				}
				if (j < 258 || k < 10)
				{
					break;
				}
			}
			c = z.AvailableBytesIn - k;
			c = ((i >> 3 < c) ? (i >> 3) : c);
			k += c;
			p -= c;
			i -= c << 3;
			s.bitb = b;
			s.bitk = i;
			z.AvailableBytesIn = k;
			z.TotalBytesIn += p - z.NextIn;
			z.NextIn = p;
			s.writeAt = q;
			return 0;
		}
	}
}
