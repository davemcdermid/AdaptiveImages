This is a C# .NET port of the PHP adapative-images script <http://adaptive-images.com/>.

Installation
------------

The project can be compiled to a DLL and dropped into any .NET 4 project. Alternatively, add this project to an existing solution. The project can also be compiled in .NET 3.5 compatibility if required.

Add the handler references from the sample.web.config to the web.config file for your website.

Add the following javascript to set a cookie with the screen resolution:

    <script type="text/javascript">document.cookie = 'resolution=' + Math.max(screen.width, screen.height) + '; path=/';</script>

