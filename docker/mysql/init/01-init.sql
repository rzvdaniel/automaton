-- Initialization script for Automaton MySQL database
-- This script runs only on first container startup

-- The database and user are already created by environment variables
-- This file can be used for additional initialization if needed

-- Set timezone
SET time_zone = '+00:00';

-- Ensure utf8mb4 is used
ALTER DATABASE automaton CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci;

-- Grant additional privileges if needed
-- GRANT ALL PRIVILEGES ON automaton.* TO 'automaton_user'@'%';
-- FLUSH PRIVILEGES;

-- Create additional users or roles here if needed for team members
-- Example:
-- CREATE USER IF NOT EXISTS 'developer'@'%' IDENTIFIED BY 'DevPass123!';
-- GRANT SELECT, INSERT, UPDATE, DELETE ON automaton.* TO 'developer'@'%';

SELECT 'Automaton MySQL database initialized successfully' AS message;
