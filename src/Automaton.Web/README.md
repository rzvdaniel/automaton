# Automaton.Web - Presentation Website

This is a tech-focused presentation website for Automaton, built with Blazor Server to showcase the platform's key features and capabilities.

## Overview

The Automaton.Web project provides a modern, developer-oriented marketing website that:

- Introduces the Automaton platform and its core features
- Links to the main Automaton Studio at https://studio.automaton.monster
- Provides documentation links to https://studio.automaton.monster/docs
- Uses a dark, code-inspired theme with gradient accents
- Features tech imagery from Unsplash for visual appeal

## Features

### Pages
- **Homepage**: Hero section with key value propositions and feature overview
- **Features Page**: Detailed feature descriptions organized by category
- **Error Page**: Custom styled error handling

### Design Elements
- Dark theme with blue/cyan accents optimized for developers
- Tech-focused color scheme with gradients
- Code snippet preview on homepage
- Responsive grid layout for features
- Clean, minimal navigation
- Custom CSS variables for consistent theming

### Technical Stack
- **Blazor Server**: .NET 9.0 web framework
- **Custom CSS**: No external CSS frameworks, pure CSS with modern features
- **Responsive Design**: Mobile-first approach
- **Modern Web Standards**: CSS Grid, Flexbox, CSS Variables

## Running the Application

```bash
cd src/Automaton.Web
dotnet run
```

The application will be available at `http://localhost:5000` by default.

## Building

```bash
cd src/Automaton.Web
dotnet build
```

## Deployment

This is a standard Blazor Server application and can be deployed to any hosting environment that supports .NET 9.0.

## Key Links

- **Try Automaton Studio**: https://studio.automaton.monster
- **Documentation**: https://studio.automaton.monster/docs

## Project Structure

```
Components/
├── Layout/
│   └── MainLayout.razor          # Main layout with navigation
├── Pages/
│   ├── Home.razor               # Homepage with hero and features
│   ├── Features.razor           # Detailed features page
│   └── Error.razor              # Custom error page
├── App.razor                    # Root application component
└── Routes.razor                 # Routing configuration

wwwroot/
└── app.css                      # Custom styling with tech theme
```

## Customization

The website uses CSS custom properties (variables) defined in `wwwroot/app.css` for easy theme customization:

- `--primary-color`: Main brand color
- `--accent-color`: Highlight color for interactive elements
- `--dark-bg`: Primary background color
- `--text-light`: Main text color
- `--gradient-primary`: Brand gradient used for headings and buttons