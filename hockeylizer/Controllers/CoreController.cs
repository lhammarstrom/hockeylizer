﻿﻿﻿﻿﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using hockeylizer.Services;
using hockeylizer.Helpers;
using hockeylizer.Models;
using hockeylizer.Data;
using System.Linq;
using System.IO;
using System;
    using System.Diagnostics;
    using Hangfire;

namespace hockeylizer.Controllers
{
    public class CoreController : Controller
    {
        private readonly string _appkey;
        private readonly ApplicationDbContext _db;
        private readonly IHostingEnvironment _hostingEnvironment;

        public CoreController(ApplicationDbContext db, IHostingEnvironment hostingEnvironment)
        {
            _appkey = "langY6fgXWossV9o";
            this._db = db;
            this._hostingEnvironment = hostingEnvironment;
        }

		[HttpPost]
		[AllowAnonymous]
        public JsonResult CreateTeam([FromBody]CreateTeamVm vm) 
        {
            CreateTeamResult response;

            if (vm.token != _appkey)
            {
                response = new CreateTeamResult(false, "Laget kunde inte skapas då fel token angavs. Appen är inte registrerad.");
            }
            else 
            {
                var teamId = _db.GetAvailableTeamId();

                var team = new AppTeam(teamId);
                _db.AppTeams.Add(team);

                _db.SaveChanges();

                response = new CreateTeamResult(true, "Laget skapades. Appen är nu registrerad.")
                {
                    TeamId = team.TeamId
                };
            }

            return Json(response);
        }

        // Add player
        [HttpPost]
        [AllowAnonymous]
        public JsonResult AddPlayer([FromBody]AddPlayerVm vm)
        {
            AddPlayerResult response;

            if (vm.token == _appkey)
            {
                if (string.IsNullOrEmpty(vm.name) || string.IsNullOrWhiteSpace(vm.name))
                {
                    response = new AddPlayerResult("Spelaren " + vm.name + " kunde inte läggas till då namnet saknas.", false);
                    return Json(response);
                }

                if (vm.teamId == null)
                {
                    response = new AddPlayerResult("Spelaren " + vm.name + " kunde inte läggas till då requesten saknar appid", false);
                    return Json(response);
                }

                var team = _db.AppTeams.Find(vm.teamId);
                if (vm.teamId == null || team == null)
                {
                    response = new AddPlayerResult("Spelaren " + vm.name + " kunde inte läggas till då appteamet som hen tillhör saknas.", false);
                    return Json(response);
                }

                var player = new Player(vm.name, vm.teamId);
                try
                {
                    _db.Players.Add(player);
                    _db.SaveChanges();

                    _db.Entry(player).GetDatabaseValues();

                    response = new AddPlayerResult("Spelaren " + vm.name + " lades till utan problem", true, player.PlayerId);
                }
                catch (Exception e)
                {
                    response = new AddPlayerResult("Spelaren " + vm.name + " kunde inte läggas till. Felmeddelande: " + e.Message, false);
                }
            }
            else
            {
                response = new AddPlayerResult("Token var inkorrekt", false);
            }

            return Json(response);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> UpdatePlayerName([FromBody]UpdateNameVm vm)
        {
            GeneralResult r;

            if (vm.token != _appkey)
            {
                r = new GeneralResult(false, "Token var inkorrekt");
            }
            else
            {
                if (!vm.Validate())
                {
                    return Json(vm.Result);
                }

                var player = _db.Players.Find(vm.playerId);
                if (player == null) 
                {
                    r = new GeneralResult(false, "Spelaren finns inte.");
                }
                else
                {
                    player.Name = vm.name;
                    await _db.SaveChangesAsync();

                    r = new GeneralResult(true, "Namnet uppdaterades!");
                }
            }

            return Json(r);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> DeletePlayer([FromBody]DeletePlayerVm vm)
        {
            GeneralResult r;

            if (vm.token != _appkey)
            {
                r = new GeneralResult(false, "Token var inkorrekt");
            }
            else
            {
                if (!vm.Validate())
                {
                    return Json(vm.Result);
                }

                var player = _db.Players.Find(vm.playerId);
                if (player == null)
                {
                    r = new GeneralResult(false, "Spelaren finns inte.");
                }
                else
                {
                    player.Deleted = true;
                    await _db.SaveChangesAsync();

                    r = new GeneralResult(true, "Spelaren raderades!");
                }
            }

            return Json(r);
        }

        [HttpPost]
        [AllowAnonymous]
        public JsonResult GetAllPlayers([FromBody]GetAllPlayersVm vm)
        {
            GetPlayersResult response;
            if (vm.token == _appkey)
            {
                if (vm.teamId == null)
                {
					response = new GetPlayersResult(false, "teamId saknas i requesten", new List<PlayerVmSmall>());
					return Json(response);
                }
                var team = _db.AppTeams.Find(vm.teamId);

                if (team == null)
                {
					// Team missing
                    response = new GetPlayersResult(false, "Appen saknas i databasen", new List<PlayerVmSmall>());
                    return Json(response);
				}

                response = new GetPlayersResult(true, "Alla spelare hämtades",
                                _db.Players.Where(p => p.TeamId == vm.teamId && !p.Deleted).Select(p =>
                                  new PlayerVmSmall
                                  {
                                      PlayerId = p.PlayerId,
                                      Name = p.Name
                                  }).ToList());
                return Json(response);
            }

            response = new GetPlayersResult(false, "Token var inkorrekt", new List<PlayerVmSmall>());
         
            return Json(response);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> CreateSession(CreateSessionVm vm)
        {
            SessionResult sr;

            if (vm.token == _appkey)
            {
                if (vm.playerId == null)
                {
                    sr = new SessionResult("SpelarId saknas i requesten.", false);
                    return Json(sr);
                }

                var pl = _db.Players.Find(vm.playerId);

                if (pl == null)
                {
                    sr = new SessionResult("Spelaren kunde inte hittas.", false);
                }
                else
                {
                    if (!vm.Validate())
                    {
                        return Json(vm.sr);
                    }

                    var v = await FileHandler.UploadVideo(vm.video, pl.RetrieveContainerName(), "video");

                    if (string.IsNullOrEmpty(v.FilePath))
                    {
                        sr = new SessionResult("Videoklippet kunde inte laddas upp då något gick knas vid uppladdning!", false);
                    }
                    else
                    {
                        var savedSession = new PlayerSession(v.FilePath, v.FileName, (int)vm.playerId, (int)vm.interval, (int)vm.rounds, (int)vm.shots, (int)vm.numberOfTargets);
                        _db.Sessions.Add(savedSession);

                        savedSession.AddTargets(vm.targetOrder, vm.targetCoords, vm.timestamps);

                        _db.SaveChanges();
                        _db.Entry(savedSession).GetDatabaseValues();

                        BackgroundJob.Enqueue(() => 
                            this.ChopAlyzeSession(new SessionVm
                            {
                                sessionId = savedSession.SessionId,
                                token = _appkey
                            }
                        ));

                        sr = new SessionResult("Videoklippet laddades upp!", true, savedSession.SessionId);
                    }
                }
            }
            else
            {
                sr = new SessionResult("Token var inkorrekt.", false);
            }

            return Json(sr);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> DeleteVideoFromSession([FromBody]SessionVm vm)
        {
            GeneralResult response;
            if (vm.token == _appkey)
            {
                var session = _db.Sessions.Find(vm.sessionId);

                if (session == null)
                {
                    response = new GeneralResult(false, "Sessionen kunde inte hittas");
                    return Json(response);
                }

                var deleted = FileHandler.DeleteVideo(session.VideoPath, session.Player.RetrieveContainerName());

                if (deleted)
                {
                    session.Delete();
                    await _db.SaveChangesAsync();

                    response = new GeneralResult(true, "Videoklippet raderades");
                    return Json(response);
                }

                response = new GeneralResult(false, "Videoklippet kunde inte raderas");
                return Json(response);
            }

            response = new GeneralResult(false, "Inkorrekt token");
            return Json(response);
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<JsonResult> IsAnalyzed([FromBody]SessionVm vm)
        {
            IsAnalyzedResult response;
            if (vm.token == _appkey)
            {
                var session = _db.Sessions.Find(vm.sessionId);

                if (session == null)
                {
                    response = new IsAnalyzedResult(false, "Sessionen kunde inte hittas", false);
                    return Json(response);
                }

                response = new IsAnalyzedResult(false, "Videoklippet kunde inte raderas", true);
                return Json(response);
            }

            response = new IsAnalyzedResult(false, "Inkorrekt token", false);
            return Json(response);
        }

        [HttpPost]
		[AllowAnonymous]
		public JsonResult GetFramesFromShot([FromBody]GetTargetFramesVm vm)
        {
			GetFramesFromShotResult response;
			if (vm.token == _appkey)
			{
				var session = _db.Sessions.Find(vm.sessionId);

				if (session == null)
				{
					response = new GetFramesFromShotResult(false, "Sessionen kunde inte hittas");
					return Json(response);
				}

				if (!vm.Validate())
				{
					return Json(vm.gr);
				}

				var shot = _db.Targets.FirstOrDefault(t => t.SessionId == session.SessionId && t.Order == vm.shot);

				if (shot == null)
				{
					response = new GetFramesFromShotResult(false, "Skottet kunde inte hittas.");
					return Json(response);
				}

                response = new GetFramesFromShotResult(true, "Skottets träffpunkter har hämtats!", _db.Frames.Where(f => f.TargetId == shot.TargetId).Select(f => f.FrameUrl).ToList())
                {
                    XCoordinate = shot.XCoordinateAnalyzed,
                    YCoordinate = shot.XCoordinateAnalyzed
                };

				return Json(response);
			}

			response = new GetFramesFromShotResult(false, "Inkorrekt token");
			return Json(response);
        }

		[HttpPost]
		[AllowAnonymous]
		public JsonResult GetDataFromShot([FromBody]GetTargetFramesVm vm)
		{
			GetDataFromShotResult response;
			if (vm.token == _appkey)
			{
				var session = _db.Sessions.Find(vm.sessionId);

				if (session == null)
				{
					response = new GetDataFromShotResult(false, "Sessionen kunde inte hittas");
					return Json(response);
				}

				if (!vm.Validate())
				{
					return Json(vm.gr);
				}

				var shot = _db.Targets.FirstOrDefault(t => t.SessionId == session.SessionId && t.Order == vm.shot);

                if (shot == null)
				{
					response = new GetDataFromShotResult(false, "Skottet som skulle uppdateras kunde inte hittas.");
					return Json(response);
				}

				response = new GetDataFromShotResult(true, "Skottets träffpunkt har uppdaterats!", shot.FramesToAnalyze.Select(frame => frame.FrameUrl).ToList())
                {
                    TargetNumber = shot.TargetNumber,
                    Order = shot.Order,
                    XCoordinate = shot.XCoordinate,
                    YCoordinate = shot.YCoordinate
                };

				return Json(response);
			}

			response = new GetDataFromShotResult(false, "Inkorrekt token");
			return Json(response);
		}

		[HttpPost]
		[AllowAnonymous]
		public async Task<JsonResult> UpdateTargetHit([FromBody]UpdateTargetHitVm vm)
        {
			GeneralResult response;
			if (vm.token == _appkey)
			{
				var session = _db.Sessions.Find(vm.sessionId);

				if (session == null)
				{
					response = new GeneralResult(false, "Sessionen kunde inte hittas");
					return Json(response);
				}

                if (!vm.Validate())
				{
					return Json(vm.ur);
				}

                var shotToUpdate = _db.Targets.FirstOrDefault(t => t.SessionId == session.SessionId && t.Order == vm.shot);

                if (shotToUpdate == null)
                {
					response = new GeneralResult(false, "Skottet som skulle uppdateras kunde inte hittas.");
					return Json(response);
                }

                shotToUpdate.XCoordinate = vm.x;
                shotToUpdate.YCoordinate = vm.y;

                await _db.SaveChangesAsync();

                response = new GeneralResult(true, "Skottets träffpunkt har uppdaterats!");
				return Json(response);
			}

			response = new GeneralResult(false, "Inkorrekt token");
			return Json(response);
        }

        [HttpPost]
        [AllowAnonymous]
        public JsonResult GetFramesFromSession([FromBody]SessionVm vm)
        {
            GetFramesResult response;
            if (vm.token == _appkey)
            {
                var session = _db.Sessions.Find(vm.sessionId);

                if (session != null && !session.Deleted)
                {
                    response = new GetFramesResult(true, "Alla frames hämtades", new List<string>());

                    foreach (var t in _db.Targets.Where(tar => tar.SessionId == session.SessionId))
                    {
                        response.Images.AddRange(t.FramesToAnalyze.Select(frame => frame.FrameUrl));
                    }
                }
                else
                {
                    response = new GetFramesResult(false, "Videon finns inte", new List<string>());
                }
            }
            else
            {
                response = new GetFramesResult(false, "Token var inkorrekt", new List<string>());
            }

            return Json(response);
        }

        // Chop and analyze session
        private async void ChopAlyzeSession(SessionVm vm)
        {
            if (vm.token != _appkey) return;
            var session = _db.Sessions.Find(vm.sessionId);

            if (session == null) return;

            var blobname = session.FileName;
            var path = Path.Combine(_hostingEnvironment.WebRootPath, "videos");
            path = Path.Combine(path, blobname);

            var player = _db.Players.Find(session.PlayerId);
            if (player == null) return;

            var download = await FileHandler.DownloadBlob(path, blobname, player.RetrieveContainerName());
            if (!download) return;

            // Analyze the video
            this.AnalyzeVideo(session, path);

            var intervals = session.Targets.Select(t => new DecodeInterval
            {
                startMs = t.TimestampStart,
                endMs = t.TimestampEnd
            }).ToArray();

            var shots = AnalysisBridge.DecodeFrames(path, BlobCredentials.AccountName, BlobCredentials.Key, player.RetrieveContainerName(), intervals);
            if (shots.Any())
            {
                foreach (var s in shots)
                {
                    var target = _db.Targets.FirstOrDefault(shot => shot.SessionId == session.SessionId && shot.Order == s.Shot);

                    if (target == null) continue;
                    foreach (var frame in s.Uris)
                    {
                        var picture = new FrameToAnalyze(target.TargetId, frame);
                        await _db.Frames.AddAsync(picture);
                    }
                }

                await _db.SaveChangesAsync();
            }

            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }

        private void AnalyzeVideo(PlayerSession session, string path)
        {
            var targets = _db.Targets.Where(shot => shot.SessionId == session.SessionId).ToArray();

            var points = new Point2i[] {};
            var offsets = new Point2d[] { };

            for (var hp = 0; hp < targets.Length; hp++)
            {
                points[hp] = new Point2i(targets[hp].XCoordinate ?? 0, targets[hp].YCoordinate ?? 0);

                offsets[hp] = new Point2d(targets[hp].XCoordinate ?? 0, targets[hp].YCoordinate ?? 0);
            }

            const int width = 1;
            const int height = 1;

            foreach (var t in targets)
            {
                var analysis = AnalysisBridge.AnalyzeShot(t.TimestampStart, t.TimestampEnd, points, width, height,
                    offsets, path);

                if (analysis.WasErrors) continue;

                var xHit = analysis.HitPoint.x;
                var yHit = analysis.HitPoint.y;
                var hit = analysis.DidHitGoal;

                t.XCoordinateAnalyzed = xHit;
                t.YCoordinateAnalyzed = yHit;
                t.HitGoal = hit;
            }

            session.Analyzed = true;

            _db.SaveChanges();
        }

        /*
         * DANIELS BILDGENERERANDE FUNKTIONER
         */
        //Daniels funktion
        [HttpPost]
        [AllowAnonymous]
        public JsonResult getHitsOverviewSVG(int videoId, string token)
        {

            // Just return a goddamn picture of the goal with no hits.
            // It's something.
            var svgURL = @"http://hockeylizer.azurewebsites.net/images/hitsOverview.svg";
            return Json(svgURL);
        }

        //TODO: THIS FUNCTION NEEDS TO BE using Svg;
        [HttpPost]
        [AllowAnonymous]
        public VirtualFileResult getHitsOverviewPNG(int sessionId, string token)
        {
            // very TEMP
            var pngPlaceholderPath = _hostingEnvironment.WebRootPath + "/images/hitsOverview.png";
            return base.File(pngPlaceholderPath, "image/png");

            //GeneralResult response;
            //if (token == _appkey)
            //{
                

            //    var hitList = _db.Targets.Where(target => target.SessionId == sessionId);

            //    // Lite osäker på om ett query utan resultat ger
            //    // void eller en enumarable av längd noll.
            //    if (hitList == null || hitList.Count() == 0)
            //    {
            //        response = new GeneralResult(false, "Analysen finns inte");
            //        return Json(response);
            //    }

            //    var svgPath = _hostingEnvironment.WebRootPath + "/images/hitsOverview.svg";
                
            //    string svgStr = System.IO.File.ReadAllText(svgPath);

            //    // Svg stuff needs the svg-library, installable from nuget.
            //    SvgDocument svgDocument = SvgDocument.FromSvg<SvgDocument>(svgStr);
            //    foreach (var hit in hitList)
            //    {
            //        SvgCircle circle = new SvgCircle();
            //        circle.CenterX = hit.XCoordinate;
            //        circle.CenterY = hit.YCoordinate;
            //        circle.Radius = 4;
            //        circle.Fill = new SvgColourServer(System.Drawing.Color.Black);
            //        svgDocument.Children.Add(circle);
            //    }

            //    using (var bitmap = svgDocument.Draw())
            //    {
            //        bitmap.Save(@"..\..\goal.png", ImageFormat.Png);
            //    }

            //}

            //response = new GeneralResult(false, "Inkorrekt token");
            //return Json(response);
        }
    }
}