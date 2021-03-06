﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using GonzoNet;
using GonzoNet.Encryption;
using LoginDataModel;
using LoginDataModel.Entities;
using ProtocolAbstractionLibraryD;

namespace TSO_LoginServer.Network
{
	class LoginPacketHandlers
	{
		/// <summary>
		/// Client wanted to log in!
		/// </summary>
		public static void HandleLoginRequest(NetworkClient Client, ProcessedPacket P)
		{
			try
			{
				Logger.LogInfo("Received LoginRequest!\r\n");

				byte Version1 = (byte)P.ReadByte();
				byte Version2 = (byte)P.ReadByte();
				byte Version3 = (byte)P.ReadByte();
				byte Version4 = (byte)P.ReadByte();

				string ClientVersion = Version1.ToString() + "." + Version2.ToString() + "." + Version3.ToString() +
					"." + Version4.ToString();

				if (ClientVersion != GlobalSettings.Default.ClientVersion)
				{
					PacketStream OutPacket = new PacketStream((byte)PacketType.INVALID_VERSION, 2);
					OutPacket.WriteHeader();
					OutPacket.WriteByte(0x01);
					Client.Send(OutPacket.ToArray());

					Logger.LogInfo("Bad version - sent SInvalidVersion!\r\n");
					Client.Disconnect();
					return;
				}

				PacketStream EncryptedPacket = new PacketStream((byte)PacketType.LOGIN_NOTIFY, 0);
				EncryptedPacket.WriteHeader();

				AESEncryptor Enc = (AESEncryptor)Client.ClientEncryptor;

				if (Enc == null)
					Enc = new AESEncryptor("");

				Enc.PublicKey = P.ReadBytes((P.ReadByte()));
				Enc.NOnce = P.ReadBytes((P.ReadByte()));
				Enc.PrivateKey = NetworkFacade.ServerKey;
				Client.ClientEncryptor = Enc;

				MemoryStream StreamToEncrypt = new MemoryStream();
				BinaryWriter Writer = new BinaryWriter(StreamToEncrypt);
				Writer.Write(Enc.Challenge, 0, Enc.Challenge.Length);
				Writer.Flush();

				byte[] EncryptedData = StaticStaticDiffieHellman.Encrypt(NetworkFacade.ServerKey,
					System.Security.Cryptography.ECDiffieHellmanCngPublicKey.FromByteArray(Enc.PublicKey,
					System.Security.Cryptography.CngKeyBlobFormat.EccPublicBlob), Enc.NOnce, StreamToEncrypt.ToArray());

				EncryptedPacket.WriteUInt16((ushort)(PacketHeaders.UNENCRYPTED +
					(1 + NetworkFacade.ServerPublicKey.Length) +
					(1 + EncryptedData.Length)));

				EncryptedPacket.WriteByte((byte)NetworkFacade.ServerPublicKey.Length);
				EncryptedPacket.WriteBytes(NetworkFacade.ServerPublicKey);
				EncryptedPacket.WriteByte((byte)EncryptedData.Length);
				EncryptedPacket.WriteBytes(EncryptedData);

				Client.Send(EncryptedPacket.ToArray());
			}
			//This should HOPEFULLY wade off clients sending unreadable (I.E old protocol) packets...
			catch (Exception E)
			{
				Logger.LogDebug("Error while handling login request, disconnecting client: " +
					E.ToString());
				Client.Disconnect();
				return;
			}
		}

		/// <summary>
		/// Client sent a response to our challenge, as well as account name and password.
		/// </summary>
		public static void HandleChallengeResponse(NetworkClient Client, ProcessedPacket P)
		{
			PacketStream OutPacket;

			int Length = P.ReadByte();
			byte[] CResponse;

			if (P.BufferLength >= Length)
				CResponse = P.ReadBytes(Length);
			else
				return;

			AESDecryptionArgs DecryptionArgs = Client.ClientEncryptor.GetDecryptionArgsContainer().AESDecryptArgs;

			if (DecryptionArgs.Challenge.SequenceEqual(CResponse))
			{
				string AccountName = SanitizeAccount(P.ReadString());

				Length = P.ReadByte();
				byte[] PasswordHash;

				if (P.BufferLength >= Length)
					PasswordHash = P.ReadBytes(Length);
				else
					return;

				if (AccountName == string.Empty)
				{
					OutPacket = new PacketStream((byte)PacketType.LOGIN_FAILURE, 0);
					OutPacket.WriteByte(0x01);
					Client.SendEncrypted((byte)PacketType.LOGIN_FAILURE, OutPacket.ToArray());

					Logger.LogInfo("Bad accountname - sent SLoginFailResponse!\r\n");
					Client.Disconnect();
					return;
				}

				using (var db = DataAccess.Get())
				{
					var account = db.Accounts.GetByUsername(AccountName);

					if (!GlobalSettings.Default.CreateAccountsOnLogin)
					{
						Logger.LogInfo("Done reading LoginRequest, checking account...\r\n");

						if (account == null)
						{
							OutPacket = new PacketStream((byte)PacketType.LOGIN_FAILURE, 0);
							OutPacket.WriteByte(0x01);
							Client.SendEncrypted((byte)PacketType.LOGIN_FAILURE, OutPacket.ToArray());

							Logger.LogInfo("Bad accountname - sent SLoginFailResponse!\r\n");
							Client.Disconnect();
							return;
						}
					}
					else
					{
						if (account == null)
						{
							try
							{
								db.Accounts.Create(new Account
								{
									AccountName = AccountName.ToLower(),
									Password = Convert.ToBase64String(PasswordHash)
								});
							}
							catch (Exception)
							{
								OutPacket = new PacketStream((byte)PacketType.LOGIN_FAILURE, 0);
								OutPacket.WriteByte(0x01);
								Client.SendEncrypted((byte)PacketType.LOGIN_FAILURE, OutPacket.ToArray());

								Logger.LogInfo("Bad accountname - sent SLoginFailResponse!\r\n");
								Client.Disconnect();
								return;
							}

							account = db.Accounts.GetByUsername(AccountName);
						}
					}

					if (account.IsCorrectPassword(AccountName, PasswordHash))
					{
						OutPacket = new PacketStream((byte)PacketType.LOGIN_SUCCESS, 0);
						OutPacket.WriteByte(0x01);
						Client.ClientEncryptor.Username = AccountName;
						Client.SendEncrypted((byte)PacketType.LOGIN_SUCCESS, OutPacket.ToArray());

						Logger.LogInfo("Sent SLoginSuccessResponse!\r\n");
						return;
					}
					else
					{
						OutPacket = new PacketStream((byte)PacketType.LOGIN_FAILURE, 0);
						OutPacket.WriteByte(0x02);
						Client.SendEncrypted((byte)PacketType.LOGIN_FAILURE, OutPacket.ToArray());

						Logger.LogInfo("Bad password - sent SLoginFailResponse!\r\n");
						Client.Disconnect();
						return;
					}
				}
			}

			OutPacket = new PacketStream((byte)PacketType.LOGIN_FAILURE, 0);
			OutPacket.WriteByte(0x03); //Bad challenge response.
			Client.SendEncrypted((byte)PacketType.LOGIN_FAILURE, OutPacket.ToArray());

			Logger.LogInfo("Bad challenge response - sent SLoginFailResponse!\r\n");
			Client.Disconnect();
			return;
		}

		/// <summary>
		/// Client requested information about its characters.
		/// </summary>
		public static void HandleCharacterInfoRequest(NetworkClient Client, ProcessedPacket P)
		{
			Logger.LogInfo("Received CharacterInfoRequest!");

			string DateTimeStr = P.ReadString();
			DateTime Timestamp;

			if (DateTimeStr != string.Empty)
				Timestamp = DateTime.Parse(DateTimeStr);
			else
			{
				//Unix epoch
				Timestamp = new DateTime(1970, 1, 1, 0, 0, 1);
			}

			Character[] Characters = new Character[] { };

			using (var db = DataAccess.Get())
			{
				var account = db.Accounts.GetByUsername(Client.ClientEncryptor.Username);
				Characters = db.Characters.GetForAccount((int)account.AccountID).ToArray();
			}

			int NumChars = 0, NewChars = 0;

			if (Characters != null)
			{
				PacketStream Packet = new PacketStream((byte)PacketType.CHARACTER_LIST, 0);
				MemoryStream PacketData = new MemoryStream();
				BinaryWriter PacketWriter = new BinaryWriter(PacketData);

				NumChars = Characters.Length;

				foreach (Character avatar in Characters)
				{
					//Zero means same, less than zero means T1 is earlier than T2, more than zero means T1 is later.
					if (DateTime.Compare(Timestamp, avatar.LastCached) < 0)
					{
						NewChars++;

						PacketWriter.Write((int)avatar.CharacterID);
						PacketWriter.Write(avatar.GUID.ToString());
						PacketWriter.Write(avatar.LastCached.ToString("yyyy.MM.dd hh:mm:ss",
							CultureInfo.InvariantCulture));
						PacketWriter.Write(avatar.Name);
						PacketWriter.Write(avatar.Sex);
						PacketWriter.Write(avatar.Description);
						PacketWriter.Write((ulong)avatar.HeadOutfitID);
						PacketWriter.Write((ulong)avatar.BodyOutfitID);
						PacketWriter.Write((byte)avatar.AppearanceType);
						PacketWriter.Write((string)avatar.CityName);
						PacketWriter.Write((ulong)avatar.CityThumb);
						PacketWriter.Write((string)avatar.City);
						PacketWriter.Write((ulong)avatar.CityMap);
						PacketWriter.Write((string)avatar.CityIp);
						PacketWriter.Write((int)avatar.CityPort);
					}
				}

				//NOTE: If Characters != null, but no chars were new, NumChars will be however many characters,
				//      and NewChars will be 0.

				Packet.WriteByte((byte)NumChars);
				Packet.WriteByte((byte)NewChars);
				Packet.Write(PacketData.ToArray(), 0, (int)PacketData.Length);
				PacketWriter.Close();
				Client.SendEncrypted((byte)PacketType.CHARACTER_LIST, Packet.ToArray());
			}
			else //No characters existed for the account.
			{
				PacketStream Packet = new PacketStream(0x05, 0);
				Packet.WriteByte((byte)NumChars); //0 characters.
				Packet.WriteByte((byte)NewChars); //0 new characters.

				Client.SendEncrypted((byte)PacketType.CHARACTER_LIST, Packet.ToArray());
			}
		}

		/// <summary>
		/// Client requested information about a city.
		/// </summary>
		public static void HandleCityInfoRequest(NetworkClient Client, ProcessedPacket P)
		{
			//This packet only contains a dummy byte, don't bother reading it.
			PacketStream Packet = new PacketStream((byte)PacketType.CITY_LIST, 0);
			Packet.WriteByte((byte)NetworkFacade.CServerListener.CityServers.Count);

			if (NetworkFacade.CServerListener.CityServers.Count > 0)
			{
				lock (NetworkFacade.CServerListener.CityServers)
				{
					foreach (CityInfo CInfo in NetworkFacade.CServerListener.CityServers)
					{
						Packet.WriteString(CInfo.Name);
						Packet.WriteString(CInfo.Description);
						Packet.WriteString(CInfo.IP);
						Packet.WriteInt32(CInfo.Port);

						//Hack (?) to ensure status is written correctly.
						switch (CInfo.Status)
						{
							case CityInfoStatus.Ok:
								Packet.WriteByte(1);
								break;
							case CityInfoStatus.Busy:
								Packet.WriteByte(2);
								break;
							case CityInfoStatus.Full:
								Packet.WriteByte(3);
								break;
							case CityInfoStatus.Reserved:
								Packet.WriteByte(4);
								break;
						}

						Packet.WriteUInt64(CInfo.Thumbnail);
						Packet.WriteString(CInfo.UUID);
						Packet.WriteUInt64(CInfo.Map);
					}
				}
			}

			Client.SendEncrypted((byte)PacketType.CITY_LIST, Packet.ToArray());
		}

		/// <summary>
		/// Client created a character!
		/// </summary>
		public static void HandleCharacterCreate(NetworkClient Client, ProcessedPacket P)
		{
			Logger.LogInfo("Received CharacterCreate!");

			string AccountName = SanitizeAccount(P.ReadString());
			//Need to be variable length, because the success packet contains a token.
			PacketStream CCStatusPacket = new PacketStream((byte)PacketType.CHARACTER_CREATION_STATUS, 0);

			using (var db = DataAccess.Get())
			{
				Account Acc = db.Accounts.GetByUsername(AccountName);

				if (Acc.NumCharacters >= 3)
				{
					CCStatusPacket.WriteByte((int)LoginDataModel.Entities.CharacterCreationStatus.ExceededCharacterLimit);
					Client.SendEncrypted(CCStatusPacket.PacketID, CCStatusPacket.ToArray());

					return;
				}

				//TODO: Send GUID to client...
				var Char = new Character();
				string LastCached = P.ReadString();
				if (LastCached == string.Empty)
				{
					//TODO: Proper error...
					CCStatusPacket.WriteByte((int)LoginDataModel.Entities.CharacterCreationStatus.NameAlreadyExisted);
					Client.SendEncrypted(CCStatusPacket.PacketID, CCStatusPacket.ToArray());
					return;
				}

				Char.LastCached = ProtoHelpers.ParseDateTime(LastCached);
				Char.Name = P.ReadString();
				Char.Sex = P.ReadString();
				Char.Description = P.ReadString();
				Char.GUID = Guid.NewGuid();
				Char.HeadOutfitID = (long)P.ReadUInt64();
				Char.BodyOutfitID = (long)P.ReadUInt64();
				Char.AccountID = Acc.AccountID;
				Char.AppearanceType = P.ReadByte();
				Char.CityName = P.ReadString();
				Char.CityThumb = (long)P.ReadUInt64();
				Char.City = P.ReadString();
				Char.CityMap = (long)P.ReadUInt64();
				Char.CityIp = P.ReadString();
				Char.CityPort = P.ReadInt32();

				//These are going into DB, so be nazi. Sieg heil!
				if (Char.Name == string.Empty || Char.Sex == string.Empty ||
					Char.Description == string.Empty)
				{
					CCStatusPacket.WriteByte((int)LoginDataModel.Entities.CharacterCreationStatus.NameAlreadyExisted);
					Client.SendEncrypted(CCStatusPacket.PacketID, CCStatusPacket.ToArray());
					return;
				}

				var status = db.Characters.CreateCharacter(Char);

				switch (status)
				{
					case LoginDataModel.Entities.CharacterCreationStatus.NameAlreadyExisted:
						CCStatusPacket.WriteByte((int)LoginDataModel.Entities.CharacterCreationStatus.NameAlreadyExisted);
						Client.SendEncrypted(CCStatusPacket.PacketID, CCStatusPacket.ToArray());
						break;
					case LoginDataModel.Entities.CharacterCreationStatus.NameTooLong:
						CCStatusPacket.WriteByte((int)LoginDataModel.Entities.CharacterCreationStatus.NameTooLong);
						Client.SendEncrypted(CCStatusPacket.PacketID, CCStatusPacket.ToArray());
						break;
					case LoginDataModel.Entities.CharacterCreationStatus.Success:
						Guid Token = Guid.NewGuid();

						//This actually updates the record, not sure how.
						Acc.NumCharacters++;

						//THIS NEEDS TO HAPPEN FIRST FOR CITY SERVER AUTHENTICATION TO WORK!
						CityInfo CServer = NetworkFacade.CServerListener.GetCityServer(Char.City);

						//Just in case...
						if (CServer != null)
						{
							PacketStream CServerPacket = new PacketStream(0x01, 0);
							CServerPacket.WriteHeader();

							ushort PacketLength = (ushort)(PacketHeaders.UNENCRYPTED + 1 + 4 + (Client.RemoteIP.Length + 1)
								+ 4 + (Char.GUID.ToString().Length + 1) + (Token.ToString().Length + 1));
							CServerPacket.WriteUInt16(PacketLength);

							CServerPacket.WriteByte(1); //CharacterCreate = true
							CServerPacket.WriteInt32(Acc.AccountID);
							CServerPacket.WriteString(Client.RemoteIP);
							CServerPacket.WriteInt32(Client.RemotePort);
							CServerPacket.WriteString(Char.GUID.ToString());
							CServerPacket.WriteString(Token.ToString(""));
							CServer.Client.Send(CServerPacket.ToArray());
						}

						CCStatusPacket.WriteByte((int)LoginDataModel.Entities.CharacterCreationStatus.Success);
						CCStatusPacket.WriteString(Char.GUID.ToString());
						CCStatusPacket.WriteString(Token.ToString());
						Client.SendEncrypted(CCStatusPacket.PacketID, CCStatusPacket.ToArray());

						break;
				}
			}
		}

		/// <summary>
		/// Client wanted to transfer to a city server.
		/// </summary>
		public static void HandleCityTokenRequest(NetworkClient Client, ProcessedPacket P)
		{
			string AccountName = P.ReadString();
			string CityGUID = P.ReadString();
			string CharGUID = P.ReadString();

			if (AccountName == string.Empty || CityGUID == string.Empty || CharGUID == string.Empty)
				return;

			Guid Token = Guid.NewGuid();
			CityInfo CServer = NetworkFacade.CServerListener.GetCityServer(CityGUID);

			using (var db = DataAccess.Get())
			{
				Account Acc = db.Accounts.GetByUsername(AccountName);

				PacketStream CServerPacket = new PacketStream(0x01, 0);
				CServerPacket.WriteHeader();

				ushort PacketLength = (ushort)(PacketHeaders.UNENCRYPTED + 1 + 4 + (Client.RemoteIP.Length + 1)
					+ 4 + (CharGUID.ToString().Length + 1) + (Token.ToString().Length + 1));
				CServerPacket.WriteUInt16(PacketLength);

				CServerPacket.WriteByte(0); //CharacterCreate = false.
				CServerPacket.WriteInt32(Acc.AccountID);
				CServerPacket.WriteString(Client.RemoteIP);
				CServerPacket.WriteInt32(Client.RemotePort);
				CServerPacket.WriteString(CharGUID.ToString());
				CServerPacket.WriteString(Token.ToString(""));
				CServer.Client.Send(CServerPacket.ToArray());
			}
		}

		/// <summary>
		/// Client wanted to retire a character.
		/// </summary>
		public static void HandleCharacterRetirement(NetworkClient Client, ProcessedPacket P)
		{
			PacketStream Packet;

			string AccountName = P.ReadString();
			string GUID = P.ReadString();

			if (AccountName == string.Empty || GUID == string.Empty)
				return;

			using (var db = DataAccess.Get())
			{
				Account Acc = db.Accounts.GetByUsername(AccountName);
				IQueryable<Character> Query = db.Characters.GetForAccount(Acc.AccountID);

				//FUCK, I hate LINQ.
				Guid CharGUID = new Guid(GUID);
				Character Char = Query.Where(x => x.GUID == CharGUID).SingleOrDefault();

				if (Char != null)
					db.Characters.RetireCharacter(Char);
				else
					return;

				//This actually updates the record, not sure how.
				Acc.NumCharacters--;

				if (Char != null)
				{
					CityInfo CInfo = NetworkFacade.CServerListener.GetCityServer(Char.City);

					//Just in case...
					if (CInfo != null)
					{
						Packet = new PacketStream(0x02, 0);
						Packet.WriteHeader();

						ushort PacketLength = (ushort)(PacketHeaders.UNENCRYPTED + 4 + GUID.Length + 1);

						Packet.WriteUInt16(PacketLength);
						Packet.WriteInt32(Acc.AccountID);
						Packet.WriteString(GUID);
						CInfo.Client.Send(Packet.ToArray());
					}
				}
			}

			Packet = new PacketStream((byte)PacketType.RETIRE_CHARACTER_STATUS, 0);
			Packet.WriteString(GUID);
			Client.SendEncrypted((byte)PacketType.RETIRE_CHARACTER_STATUS, Packet.ToArray());
		}

		private static string SanitizeAccount(string AccountName)
		{
			return AccountName.Replace("İ", "I");
		}
	}
}