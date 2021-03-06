using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Caching;

namespace ImageManager
{
	public class ImageService : IImageService
	{
		public HttpContext Context { get; set; }

		public ImageService(HttpContext context)
		{
			Context = context;
		}

		public bool SaveForWeb(string sourceFileName, string relativeSourcePath, string relativeTargetPath)
		{
			var sourceFilePath = Context.Server.MapPath(relativeSourcePath + sourceFileName);
			var image = Image.FromFile(sourceFilePath);
			var scaleFactor = getScaleFactor(image, Configs.MaxImageDimension);

			var defaultWidth = scaleFactor < 1 ? (int)(image.Width * scaleFactor) : image.Width;
			var defaultHeight = scaleFactor < 1 ? (int)(image.Height * scaleFactor) : image.Height;

			var thumbnailImage = createThumbnail(image, defaultWidth, defaultHeight);
			var targetFilePath = Context.Server.MapPath(relativeTargetPath + sourceFileName);

			if (File.Exists(targetFilePath))
				File.Delete(targetFilePath);

			thumbnailImage.Save(targetFilePath, ImageFormat.Png);

			image.Dispose();
			thumbnailImage.Dispose();
			return true;
		}

		private Bitmap createThumbnail(Image image, int defaultWidth, int defaultHeight)
		{
			var thumbBmp = new Bitmap(defaultWidth, defaultHeight);
			thumbBmp.SetResolution(image.HorizontalResolution, image.VerticalResolution);
			var graphics = Graphics.FromImage(thumbBmp);
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

			var imageAttributes = new ImageAttributes();
			imageAttributes.SetWrapMode(WrapMode.TileFlipXY);

			var destRectangle = new Rectangle(0, 0, defaultWidth, defaultHeight);
			graphics.DrawImage(image, destRectangle, 0, 0, image.Width, image.Height,
				GraphicsUnit.Pixel, imageAttributes);
			return thumbBmp;
		}

		public byte[] Get(string relativeFilePath, int width, int height, ImageMod imageMod, string hexBackgroundColour, AnchorPosition? anchor, OutputFormat outputFormat)
		{
			using (var bitmap = Get(relativeFilePath, width, height, imageMod, hexBackgroundColour, anchor))
			{
				return bitmap.GetBytes(outputFormat);
			}
		}
		public Bitmap Get(string relativeFilePath, int width, int height, ImageMod imageMod, string hexBackgroundColour, AnchorPosition? anchor)
		{
			using (var image = (relativeFilePath == "Default" ? getDefault(width, height) : loadImage(relativeFilePath) ?? getDefault(width, height)))
			{
				switch (imageMod)
				{
					case ImageMod.Scale:
						return scale(image, width, height, hexBackgroundColour);
					case ImageMod.Crop:
						return crop(image, width, height, anchor ?? AnchorPosition.Center);
					default:
						return scale(image, width, height, hexBackgroundColour);
				}
			}
		}

		public byte[] Get(string relativeFilePath, int maxSideSize, OutputFormat outputFormat)
		{
			using (var bitmap = Get(relativeFilePath, maxSideSize))
			{
				return bitmap.GetBytes(outputFormat);
			}
		}
		public Bitmap Get(string relativeFilePath, int maxSideSize)
		{
			Func<int, Image> defaultImage = maxSize => getDefault(maxSize, maxSize);

			var image = (relativeFilePath == "Default" ? defaultImage(maxSideSize) :
				loadImage(relativeFilePath)) ?? defaultImage(maxSideSize);

			if (image.Width < maxSideSize & image.Height < maxSideSize)
			{
				maxSideSize = image.Width > image.Height ? image.Width : image.Height;
			}

			var scaleFactor = getScaleFactor(image, maxSideSize);
			var width = Convert.ToInt32(scaleFactor * image.Width);
			var height = Convert.ToInt32(scaleFactor * image.Height);

			var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);

			bitmap.SetResolution(image.HorizontalResolution, image.VerticalResolution);
			var graphics = Graphics.FromImage(bitmap);

			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.CompositingQuality = CompositingQuality.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.CompositingMode = CompositingMode.SourceCopy;

			graphics.DrawImage(image, 0, 0, width, height);

			graphics.Dispose();
			return bitmap;
		}

		public byte[] Get(string relativeFilePath, int maxWidth, int maxHeight, OutputFormat outputFormat)
		{
			using (var bitmap = Get(relativeFilePath, maxWidth, maxHeight))
			{
				return bitmap.GetBytes(outputFormat);
			}
		}
		public Bitmap Get(string relativeFilePath, int maxWidth, int maxHeight)
		{
			var image = (relativeFilePath == "Default" ? getDefault(maxWidth, maxHeight) :
				loadImage(relativeFilePath)) ?? getDefault(maxWidth, maxHeight);

			if (image.Width < maxWidth && image.Height < maxHeight)
			{
				maxWidth = image.Width;
				maxHeight = image.Height;
			}
			var widthScaleFactor = (float)maxWidth / image.Width;
			var heightScaleFactor = (float)maxHeight / image.Height;
			var scaleFactor = widthScaleFactor > heightScaleFactor ? heightScaleFactor : widthScaleFactor;

			var width = Convert.ToInt32(scaleFactor * image.Width);
			var height = Convert.ToInt32(scaleFactor * image.Height);

			var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);

			bitmap.SetResolution(image.HorizontalResolution, image.VerticalResolution);
			var graphics = Graphics.FromImage(bitmap);

			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.CompositingQuality = CompositingQuality.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.CompositingMode = CompositingMode.SourceCopy;

			graphics.DrawImage(image, 0, 0, width, height);

			graphics.Dispose();
			return bitmap;
		}

		private Image loadImage(string relativeFilePath)
		{
			var physicalPath = Context.Server.MapPath(relativeFilePath);
			return !File.Exists(physicalPath) ? null : Image.FromFile(physicalPath);
		}

		private Bitmap getDefault(int width, int height)
		{
			var defaultImage = new Bitmap(width, height);
			using (var g = Graphics.FromImage(defaultImage))
			{
				g.Clear(Color.Gray);
			}
			return defaultImage;
		}

		public byte[] GetCached(string relativeFilePath, int width, int height, ImageMod imageMod, string hexBackgroundColour, AnchorPosition? anchor, OutputFormat outputFormat)
		{
			var key = string.Format("ImageManager-{0}-{1}-{2}-{3}-{4}", relativeFilePath, width, height, imageMod, outputFormat);
			if (Context.Cache[key] == null)
			{
				var image = Get(relativeFilePath, width, height, imageMod, hexBackgroundColour, anchor, outputFormat);
				if (image == null) throw new FileNotFoundException("The image requested does not exist.");
				Context.Cache.Insert(key, image, null, Cache.NoAbsoluteExpiration, Configs.CacheExpiration);
			}
			return (byte[])Context.Cache[key];
		}

		public void Delete(string fullFilePath)
		{
			if (File.Exists(fullFilePath))
			{
				File.Delete(fullFilePath);
			}
		}

		///<summary> 
		/// This returns a specified crop
		/// </summary>
		/// <param name="relativeFilePath">e.g. asdf/asdf.jpg</param>
		public byte[] GetAndCrop(string relativeFilePath, int targetWidth, int targetHeight, double widthRatio, double heightRatio, double leftRatio, double topRatio, OutputFormat outputFormat)
		{
			return GetAndCrop(relativeFilePath, targetWidth, targetHeight, widthRatio, heightRatio, leftRatio, topRatio).GetBytes(outputFormat);
		}

		///<summary> 
		/// This returns a specified crop
		/// </summary>
		/// <param name="relativeFilePath">e.g. asdf/asdf.jpg</param>
		public Bitmap GetAndCrop(string relativeFilePath, int targetWidth, int targetHeight, double widthRatio, double heightRatio, double leftRatio, double topRatio)
		{
			var sourceImage = loadImage(relativeFilePath);

			if (sourceImage == null) return getDefault(targetWidth, targetHeight);

			//target
			var bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
			bitmap.SetResolution(sourceImage.HorizontalResolution, sourceImage.VerticalResolution);

			var graphics = Graphics.FromImage(bitmap);

			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.CompositingQuality = CompositingQuality.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.CompositingMode = CompositingMode.SourceCopy;

			graphics.DrawImage(sourceImage,

				new Rectangle(0, 0,
					targetWidth,
					targetHeight)
					,
				new Rectangle(
					Convert.ToInt32(leftRatio * sourceImage.Width),
					Convert.ToInt32(topRatio * sourceImage.Height),
					Convert.ToInt32(widthRatio * sourceImage.Width),
					Convert.ToInt32(heightRatio * sourceImage.Height)),

				GraphicsUnit.Pixel);

			graphics.Dispose();
			return bitmap;
		}


		private Bitmap crop(Image image, int width, int height, AnchorPosition Anchor)
		{
			var sourceWidth = image.Width;
			var sourceHeight = image.Height;
			var sourceX = 0;
			var sourceY = 0;
			var destX = 0;
			var destY = 0;

			float nPercent;
			float nPercentW;
			float nPercentH;

			nPercentW = (width / (float)sourceWidth);
			nPercentH = (height / (float)sourceHeight);

			if (nPercentH < nPercentW)
			{
				nPercent = nPercentW;
				switch (Anchor)
				{
					case AnchorPosition.Top:
						destY = 0;
						break;
					case AnchorPosition.Bottom:
						destY = (int)(height - Math.Round(sourceHeight * nPercent));
						break;
					default:
						destY = (int)((height - Math.Round(sourceHeight * nPercent)) / 2);
						break;
				}
			}
			else
			{
				nPercent = nPercentH;
				switch (Anchor)
				{
					case AnchorPosition.Left:
						destX = 0;
						break;
					case AnchorPosition.Right:
						destX = (int)(width - Math.Round(sourceWidth * nPercent));
						break;
					default:
						destX = (int)((width - Math.Round(sourceWidth * nPercent)) / 2);
						break;
				}
			}

			var destWidth = (int)Math.Round(sourceWidth * nPercent);
			var destHeight = (int)Math.Round(sourceHeight * nPercent);

			var bmPhoto = new Bitmap(width, height, PixelFormat.Format24bppRgb);
			bmPhoto.SetResolution(image.HorizontalResolution, image.VerticalResolution);

			var grPhoto = Graphics.FromImage(bmPhoto);

			grPhoto.Clear(Utilities.BackgroundColour);
			grPhoto.PixelOffsetMode = PixelOffsetMode.HighQuality;
			grPhoto.CompositingQuality = CompositingQuality.HighQuality;
			grPhoto.InterpolationMode = InterpolationMode.HighQualityBicubic;
			grPhoto.CompositingMode = CompositingMode.SourceCopy;

			var imageAttributes = new ImageAttributes();
			imageAttributes.SetWrapMode(WrapMode.TileFlipXY);

			grPhoto.DrawImage(image,
				new Rectangle(destX, destY, destWidth, destHeight),
				sourceX, sourceY, sourceWidth, sourceHeight,
				GraphicsUnit.Pixel, imageAttributes);

			grPhoto.Dispose();
			return bmPhoto;
		}

		private Bitmap scale(Image sourcePhoto, int Width, int Height, string hexBackgroundColour)
		{
			var destinationRectangle = GetDestinationRectangle(Width, Height, sourcePhoto.Width, sourcePhoto.Height);

			var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
			bitmap.SetResolution(sourcePhoto.HorizontalResolution, sourcePhoto.VerticalResolution);

			var grPhoto = Graphics.FromImage(bitmap);

			var backgroundColour = Utilities.BackgroundColour;
			if (!string.IsNullOrEmpty(hexBackgroundColour))
			{
				backgroundColour = getColour(hexBackgroundColour);
			}

			grPhoto.Clear(backgroundColour);

			grPhoto.PixelOffsetMode = PixelOffsetMode.HighQuality;
			grPhoto.CompositingQuality = CompositingQuality.HighQuality;
			grPhoto.InterpolationMode = InterpolationMode.HighQualityBicubic;

			var imageAttributes = new ImageAttributes();
			imageAttributes.SetWrapMode(WrapMode.TileFlipXY);


			grPhoto.DrawImage(sourcePhoto, destinationRectangle, 0, 0, sourcePhoto.Width, sourcePhoto.Height,
				GraphicsUnit.Pixel, imageAttributes);

			grPhoto.Dispose();
			return bitmap;
		}

		public Rectangle GetDestinationRectangle(int width, int height, int sourceWidth, int sourceHeight)
		{
			var destX = 0;
			var destY = 0;

			float finalScalePercent;
			var widthPercent = (width / (float)sourceWidth);
			var heightPercent = (height / (float)sourceHeight);

			if (heightPercent < widthPercent)
			{
				destX = Convert.ToInt16((width - (sourceWidth * heightPercent)) / 2);
				finalScalePercent = heightPercent;
			}
			else
			{
				destY = Convert.ToInt16((height - (sourceHeight * widthPercent)) / 2);
				finalScalePercent = widthPercent;
			}

			var destWidth = (int)(sourceWidth * finalScalePercent);
			var destHeight = (int)(sourceHeight * finalScalePercent);

			return new Rectangle(destX, destY, destWidth, destHeight);
		}

		private Color getColour(string hexColour)
		{
			if (string.IsNullOrEmpty(hexColour) || hexColour.Length != 6)
				throw new ArgumentException("The string supplied should be in the hexidecimal colour format: e.g. 'AABB22' ");

			var red = int.Parse(hexColour.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
			var green = int.Parse(hexColour.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
			var blue = int.Parse(hexColour.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
			return Color.FromArgb(red, green, blue);
		}

		private float getScaleFactor(Image image, float maxDimension)
		{
			float scaleFactor;
			if (image.Width > image.Height)
			{
				scaleFactor = maxDimension / image.Width;
			}
			else
			{
				scaleFactor = maxDimension / image.Height;
			}
			return scaleFactor;
		}

		private bool thumbnailCallback()
		{
			return true;
		}
	}

	public enum AnchorPosition
	{
		Center,
		Top,
		Bottom,
		Left,
		Right
	}

	public enum ImageMod
	{
		Raw = 0,
		Scale = 1,
		Crop = 3,
		SpecifiedCrop = 4
	}

	public enum OutputFormat
	{
		Png = 0,
		Jpeg = 1,
		Gif = 2,
		HighQualityJpeg = 3
	}
}