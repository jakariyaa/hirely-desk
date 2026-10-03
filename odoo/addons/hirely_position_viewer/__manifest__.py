{
    "name": "Hirely Desk Position Viewer",
    "version": "18.0.1.0.0",
    "summary": "Read-only viewer for positions and aggregated results imported from Hirely Desk",
    "description": """
Hirely Desk Position Viewer
===========================

Imports positions and their aggregated candidate statistics (average/min/max
for numeric attributes, most popular values for text attributes, and so on)
from the Hirely Desk recruitment platform through its integration API.

Import uses a per-position API token generated in Hirely Desk. The module is a
read-only viewer: imported data cannot be created, edited, or deleted from the
Odoo user interface, and re-importing the same position updates it in place.
""",
    "author": "Hirely Desk",
    "website": "https://github.com/",
    "category": "Human Resources/Recruitment",
    "license": "LGPL-3",
    "depends": ["base"],
    "data": [
        "security/hirely_security.xml",
        "security/ir.model.access.csv",
        "data/hirely_data.xml",
        "wizard/hirely_import_wizard_views.xml",
        "views/hirely_position_views.xml",
        "views/hirely_menus.xml",
    ],
    "installable": True,
    "application": True,
    "auto_install": False,
}
