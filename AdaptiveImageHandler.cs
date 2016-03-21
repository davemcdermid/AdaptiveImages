using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.Configuration;

namespace AdaptiveImages
{
	class AdaptiveImageHandler : IHttpHandler
	{
		private int[] resolutions = { 1382, 992, 768, 480 }; // the resolution break-points to use (screen widths, in pixels)
		private bool default_raw = false; // If the resolution is larger than the largest break-point, should we sent the raw image?
		private string cache_path = "ai-cache"; // where to store the generated re-sized images. This folder must be writable.
		private long jpg_quality = 80L; // the quality of any generated JPGs on a scale of 0 to 100
		private bool watch_cache = true; // check that the responsive image isn't stale (ensures updated source images are re-cached)
		private int browser_cache = 60 * 60 * 24 * 7; // How long the BROWSER cache should last (seconds, minutes, hours, days. 7days by default)
		private bool mobile_first = true; // If there's no cookie FALSE sends the largest var resolutions version (TRUE sends smallest)
		private string cookie_name = "resolution"; // the name of the cookie containing the resolution value

		private static string[] desktop_oss = { "macintosh", "x11", "windows nt" };
		private static string[] image_exts = { ".png", ".gif", ".jpeg" };

		public AdaptiveImageHandler()
		{
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.ResolutionBreakPoints"])) {
				int[] parsed_resolutions = ConfigurationManager.AppSettings["AdaptiveImages.ResolutionBreakPoints"].Split(',').Select(e => int.Parse(e)).ToArray();
				if (parsed_resolutions.Length > 0)
					resolutions = parsed_resolutions;
			}
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.CachePath"]))
				cache_path = ConfigurationManager.AppSettings["AdaptiveImages.CachePath"];
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.JpegQuality"]))
				Int64.TryParse(ConfigurationManager.AppSettings["AdaptiveImages.JpegQuality"], out jpg_quality);
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.WatchCache"]))
				watch_cache = string.Compare(ConfigurationManager.AppSettings["AdaptiveImages.WatchCache"], "true", true) == 0;
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.BrowserCache"]))
				int.TryParse(ConfigurationManager.AppSettings["AdaptiveImages.BrowserCache"], out browser_cache);
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.MobileFirst"]))
				mobile_first = string.Compare(ConfigurationManager.AppSettings["AdaptiveImages.MobileFirst"], "true", true) == 0;
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.CookieName"]))
				cookie_name = ConfigurationManager.AppSettings["AdaptiveImages.CookieName"];
			if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AdaptiveImages.DefaultRaw"]))
				default_raw = string.Compare(ConfigurationManager.AppSettings["AdaptiveImages.DefaultRaw"], "true", true) == 0;
		}

		public bool IsReusable
		{
			get { return false; }
		}

		public void ProcessRequest(HttpContext context)
		{

			var requested_file = context.Request.RawUrl;
			var source_file = context.Server.MapPath(requested_file);
			int resolution = 0;

			//check source file exists
			if (!File.Exists(source_file))
				SendErrorImage(context, "Image not found");
			//look for cookie identifying resolution
			if (context.Request.Cookies[cookie_name] != null) {
				int client_width = 0;
				if (int.TryParse(context.Request.Cookies[cookie_name].Value, out client_width)) {
					resolution = resolutions.OrderBy(i => i).FirstOrDefault(break_point => client_width <= break_point);
					if(default_raw && client_width > resolution)
						SendImage(context, source_file, browser_cache);
				} else {
					//delete the mangled cookie
					context.Response.Cookies[cookie_name].Value = string.Empty;
					context.Response.Cookies[cookie_name].Expires = DateTime.Now;
				}
			}
			//if no resolution set, use default
			if (resolution == 0) {
				resolution = mobile_first && !BrowserDetect(context) ? resolutions.Min() : resolutions.Max();
			}
			//map path to cached file
			string cache_file = context.Server.MapPath(string.Format("/{0}/{1}/{2}", cache_path, resolution, requested_file));
			//send image
			try {
				if (File.Exists(cache_file)) { // it exists cached at that size
					if (watch_cache) { // if cache watching is enabled, compare cache and source modified dates to ensure the cache isn't stale
						cache_file = RefreshCache(source_file, cache_file, resolution);
					}
					//send cached image
					SendImage(context, cache_file, browser_cache);
				} else {
					string file = GenerateImage(source_file, cache_file, resolution);
					SendImage(context, file, browser_cache);
				}
			} catch (Exception ex) { // send exception message as image
				SendErrorImage(context, ex.Message);
			}
		}

		/// <summary>Switch off mobile-first if browser is identifying as desktop</summary>
		private bool BrowserDetect(HttpContext context)
		{
			string userAgent = context.Request.UserAgent.ToLower();

			// Identify the OS platform. Match only desktop OSs
			return desktop_oss.Any(os => userAgent.Contains(os));
		}

		/// <summary>Sends an image to the client with caching enabled</summary>
		private void SendImage(HttpContext context, string filename, int browser_cache)
		{
			string extension = Path.GetExtension(filename).ToLower();
			context.Response.ContentType = "image/" + (image_exts.Contains(extension) ? extension.TrimStart('.') : "jpeg");
			context.Response.Cache.SetCacheability(HttpCacheability.Private);
			context.Response.Cache.SetMaxAge(TimeSpan.FromSeconds(browser_cache));
			context.Response.ExpiresAbsolute = DateTime.UtcNow.AddSeconds(browser_cache);
			context.Response.TransmitFile(filename);
		}

		/// <summary>Sends an image to the client with the specified message</summary>
		private void SendErrorImage(HttpContext context, string message)
		{
			using (Bitmap error_image = new Bitmap(800, 200)) {
				Font font = new Font("Arial", 20, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
				using (Graphics graphics = Graphics.FromImage(error_image)) {
					graphics.Clear(Color.White);
					graphics.SmoothingMode = SmoothingMode.AntiAlias;
					graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
					graphics.DrawString(message, font, new SolidBrush(Color.FromArgb(102, 102, 102)), 0, 0);
					graphics.Flush();
				}
				context.Response.ContentType = "image/jpeg";
				context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
				context.Response.Expires = -1;
				error_image.Save(context.Response.OutputStream, ImageFormat.Jpeg);
			}
		}

		/// <summary>Deletes the cache_file if older than source_file</summary>
		private string RefreshCache(string source_file, string cache_file, int resolution)
		{
			if (File.Exists(cache_file)) {
				// not modified
				if (File.GetLastWriteTime(cache_file) >= File.GetLastWriteTime(source_file)) {
					return cache_file;
				}
				// modified, clear it
				File.Delete(cache_file);
			}
			return GenerateImage(source_file, cache_file, resolution);
		}

		/// <summary>Generates a resized image at a specified resolution from the source_file and saves to the cache_file</summary>
		private string GenerateImage(string source_file, string cache_file, int resolution)
		{
			string extension = Path.GetExtension(source_file).ToLower();
			using (Image source_image = Image.FromFile(source_file)) {
				// Check the image dimensions
				int width = source_image.Size.Width;
				int height = source_image.Size.Height;

				// Do we need to downscale the image?
				if (width <= resolution) { // no, because the width of the source image is already less than the client width
					return source_file;
				}

				// We need to resize the source image to the width of the resolution breakpoint we're working with
				float ratio = (float)height / width;
				int new_width = resolution;
				int new_height = (int)Math.Ceiling(new_width * ratio);

				using (Image scaled_image = new Bitmap(new_width, new_height)) {
					using (Graphics graphics = Graphics.FromImage(scaled_image)) {
						//Set interpolation mode, otherwise it looks rubbish.
						graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
						graphics.SmoothingMode = SmoothingMode.HighQuality;
						graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
						graphics.CompositingQuality = CompositingQuality.HighQuality;
						//Draw the original image as a scaled image
						graphics.DrawImage(source_image, new Rectangle(0, 0, new_width, new_height), new RectangleF(0, 0, width, height), GraphicsUnit.Pixel);
						graphics.Flush();
					}
					//create cache directory if it doesn't exist
					if (!Directory.Exists(Path.GetDirectoryName(cache_file)))
						Directory.CreateDirectory(Path.GetDirectoryName(cache_file));
					//save image in appropriate format
					switch (extension) {
						case ".png":
							scaled_image.Save(cache_file, ImageFormat.Png);
							break;
						case ".gif":
							scaled_image.Save(cache_file, ImageFormat.Gif);
							break;
						default:
							EncoderParameters ep = new EncoderParameters();
							ep.Param[0] = new EncoderParameter(Encoder.Quality, jpg_quality);
							scaled_image.Save(cache_file, GetEncoderForMimeType("image/jpeg"), ep);
							break;
					}
				}
			}
			return cache_file;
		}

		/// <summary>Return the ImageCodecInfo for a given mime-type</summary>
		private ImageCodecInfo GetEncoderForMimeType(string mimeType)
		{
			return ImageCodecInfo.GetImageEncoders().FirstOrDefault(e => string.Compare(e.MimeType, mimeType, true) == 0);
		}
	}
}
