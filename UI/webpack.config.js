const HtmlWebpackPlugin = require('html-webpack-plugin')
const Dotenv = require('dotenv-webpack')
const path = require('path')
const staticPath = path.resolve(__dirname, './dist')

module.exports = (env, argv) => {
  const mode = argv.mode
  console.log(`Webpack building in ${mode} mode`)

  return {
    mode: mode,
    entry: './dist/App.js',
    output: {
      path: staticPath,
      filename: '[name].js'
    },

    devServer: {
      proxy: {
        '/accounts': 'http://localhost:3000',
        '/transfers': 'http://localhost:3000',
        '/diagnostic': 'http://localhost:3000',
        '/accountHub': {
          target: 'ws://localhost:3000',
          ws: true
        }
      },
      static: staticPath
    },

    plugins: [
      new Dotenv(),
      new HtmlWebpackPlugin({
        title: 'Bank',
        template: './src/index.html',
        meta: {
          viewport: 'width=device-width, initial-scale=1'
        }
      })
    ].filter(k => k != null),

    module: {
      rules: [
        {
          test: /\.css$/i,
          use: ['style-loader', 'css-loader']
        }
      ]
    },

    optimization: {
      splitChunks: {
        cacheGroups: {
          vendor: {
            test: /[\\/]node_modules|dist[\\/]fable_modules[\\/]/,
            chunks: 'all'
          }
        }
      }
    }
  }
}
