# App Thumbnails

This directory stores uploaded background images for app cards in the Apps page.

## How it works

- Users upload background images through the App Designer's "Card Appearance" settings
- Images are stored in subdirectories named after the app ID (e.g., `{app-id}/`)
- Each image is stored with a unique GUID filename to avoid conflicts
- Images support PNG, JPG, GIF, and WebP formats (max 2MB)

## File Structure

```
app-thumbnails/
??? {app-id-1}/
?   ??? {image-guid-1}.png
?   ??? {image-guid-2}.jpg
?   ??? ...
??? {app-id-2}/
?   ??? {image-guid-3}.webp
?   ??? ...
??? README.md (this file)
```

## Git Ignore Policy

This directory is excluded from version control (see `.gitignore`) because:
- User-uploaded images are runtime artifacts, not source code
- Image files can be large and change frequently
- Images should be stored in persistent storage (file system or cloud storage)

The `README.md` file is committed to document the directory's purpose.

## Access

Uploaded images are accessible via:
- **API**: `POST /api/apps/{id}/upload-background` - Upload an image
- **URL**: `/app-thumbnails/{app-id}/{filename}` - Access uploaded image
- **Full URL**: `{BaseUrl}/app-thumbnails/{app-id}/{filename}` - From different domains

## Cleanup

Uploaded images are automatically cleaned up when:
- An app is deleted
- A background image is removed from an app's settings

Manual cleanup can be performed by deleting subdirectories for apps that no longer exist.
