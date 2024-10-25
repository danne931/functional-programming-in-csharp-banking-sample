const HtmlWebpackPlugin = require('html-webpack-plugin')
const path = require('path')

const staticPath = path.resolve(__dirname, './dist')

module.exports = {
  entry: './dist/Tests.js',

  mode: 'development',

  output: {
    path: staticPath,
    filename: '[name].js'
  },

  devServer: {
    static: staticPath
  },

  plugins: [
    new HtmlWebpackPlugin({
      title: 'Shared Client/Server Domain Tests'
    })
  ]
}
